using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.DataAccess.Database;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// Integrity check, online backup and optimization (E01-S03-T07). The check and the backup only read, so they run on
/// their own connection next to the writer; optimization writes, so it's queued on the writer.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class LibraryMaintenance(LibraryConnectionFactory connections, LibraryWriter writer) : ILibraryMaintenance
{
    /// <inheritdoc />
    public async Task<IntegrityCheckResult> CheckIntegrityAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return new IntegrityCheckResult(await SqliteBackup.CheckIntegrityAsync(connection, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqliteBackup.VacuumIntoAsync(connection, destinationPath, cancellationToken).ConfigureAwait(false);
    }

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
                }

                command.CommandText = "ANALYZE;";
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancellationToken);
}
