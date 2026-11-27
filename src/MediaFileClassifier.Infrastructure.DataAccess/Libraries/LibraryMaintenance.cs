using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// Integrity check, online backup and optimization (E01-S03-T07). The check and the backup only read, so they run on
/// their own connection next to the writer, on a thread-pool thread, because Microsoft.Data.Sqlite runs its async
/// methods synchronously. Optimization writes, so it's queued on the writer.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class LibraryMaintenance(LibraryConnectionFactory connections, LibraryWriter writer) : ILibraryMaintenance
{
    private const int TruncateAttempts = 30;
    private static readonly TimeSpan TruncateRetryDelay = TimeSpan.FromMilliseconds(100);

    /// <inheritdoc />
    public Task<IntegrityCheckResult> CheckIntegrityAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            async () =>
            {
                try
                {
                    await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
                    return new IntegrityCheckResult(await SqliteBackup.CheckIntegrityAsync(connection, cancellationToken).ConfigureAwait(false));
                }
                catch (SqliteException exception) when (SqliteBackup.IsDamaged(exception))
                {
                    // A database too damaged to check is the worst result of the check, not a failure of it.
                    return new IntegrityCheckResult([exception.Message]);
                }
            },
            cancellationToken);

    /// <inheritdoc />
    public Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        return Task.Run(
            async () =>
            {
                await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
                await SqliteBackup.VacuumIntoAsync(connection, destinationPath, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DatabaseSpace> GetSpaceAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            async () =>
            {
                await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT page_count * page_size, freelist_count * page_size FROM pragma_page_count, pragma_page_size, pragma_freelist_count;";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var file = reader.GetInt64(0);
                var free = reader.GetInt64(1);
                return new DatabaseSpace(file, free, AvailableDiskBytes(connection.DataSource));
            },
            cancellationToken);

    /// <inheritdoc />
    public Task OptimizeAsync(bool vacuum = false, CancellationToken cancellationToken = default) =>
        writer.ExecuteWithoutTransactionAsync(
            async (connection, token) =>
            {
                await using var command = connection.CreateCommand();
                if (vacuum)
                {
                    command.CommandText = "VACUUM;";
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);

                    // VACUUM goes through the WAL, so the file only shrinks once the WAL is written back and truncated.
                    // The tries wait between them, so the busy timeout doesn't: a reader that holds the WAL up would
                    // otherwise hold the writer for the timeout on every try.
                    command.CommandText = "PRAGMA busy_timeout = 0;";
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    try
                    {
                        await TruncateWalAsync(connection, TruncateAttempts, (_, delayToken) => Task.Delay(TruncateRetryDelay, delayToken), token).ConfigureAwait(false);
                    }
                    finally
                    {
                        command.CommandText = "PRAGMA busy_timeout = 5000;"; // As LibraryConnectionFactory.BusyTimeoutMilliseconds.
                        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }

                command.CommandText = "ANALYZE;";
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>
    /// Runs <c>wal_checkpoint(TRUNCATE)</c> until it isn't busy, or the attempts run out. A busy checkpoint isn't an
    /// error: it reports it in its result. It's busy while the checkpointer's passive checkpoint holds the checkpoint
    /// lock, which doesn't wait for the busy timeout, and then a single try leaves the WAL, and so the files on disk, as
    /// they were. The passive checkpoint doesn't truncate the WAL file later, so the retry is what does (MFC-1071).
    /// </summary>
    /// <param name="connection">The writer's connection.</param>
    /// <param name="attempts">How many times to try.</param>
    /// <param name="waitBeforeRetry">Waits before the next try; given the number of the try that was busy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the WAL was truncated.</returns>
    internal static async Task<bool> TruncateWalAsync(
        SqliteConnection connection,
        int attempts,
        Func<int, CancellationToken, Task> waitBeforeRetry,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            bool busy;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                busy = !await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.GetInt32(0) != 0;
            }

            if (!busy)
            {
                return true;
            }

            if (attempt < attempts)
            {
                await waitBeforeRetry(attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    private static long? AvailableDiskBytes(string databasePath)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(databasePath));
            return root is null ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
