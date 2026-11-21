using System.Globalization;
using System.Text.RegularExpressions;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// Restoring a backup over a damaged database (E02-S06-T03). The damaged database is moved aside, never deleted, and
/// every step is undone when a later one fails.
/// </summary>
internal sealed partial class LibraryStore
{
    private const string BackupTimeFormat = "yyyyMMdd-HHmmss-fff";

    /// <inheritdoc />
    public IReadOnlyList<LibraryBackup> GetBackups(string libraryDirectory)
    {
        var layout = LibraryLayout.FromDirectory(libraryDirectory);
        if (!Directory.Exists(layout.BackupsDirectory))
        {
            return [];
        }

        var backups = new List<LibraryBackup>();
        foreach (var path in Directory.EnumerateFiles(layout.BackupsDirectory, "library-v*.db"))
        {
            var match = MigrationBackupName().Match(Path.GetFileName(path));
            if (match.Success
                && int.TryParse(match.Groups["version"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                && DateTimeOffset.TryParseExact(
                    match.Groups["time"].Value,
                    BackupTimeFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var createdAt))
            {
                backups.Add(new LibraryBackup(path, createdAt, version));
            }
        }

        return [.. backups.OrderByDescending(backup => backup.CreatedAt)];
    }

    /// <inheritdoc />
    public Task<IntegrityCheckResult> CheckBackupAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        return Task.Run(
            async () =>
            {
                if (!File.Exists(backupPath))
                {
                    return new IntegrityCheckResult([$"The backup {backupPath} doesn't exist."]);
                }

                // Read-only and unpooled: the check never changes the backup and leaves no handle on it.
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = backupPath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false,
                }.ToString();
                try
                {
                    await using var connection = new SqliteConnection(connectionString);
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    return new IntegrityCheckResult(await SqliteBackup.CheckIntegrityAsync(connection, cancellationToken).ConfigureAwait(false));
                }
                catch (SqliteException exception)
                {
                    return new IntegrityCheckResult([exception.Message]);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> ReplaceDamagedDatabaseAsync(string libraryDirectory, string backupPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        return Task.Run(() => ReplaceDamagedDatabase(LibraryLayout.FromDirectory(libraryDirectory), Path.GetFullPath(backupPath)), cancellationToken);
    }

    /// <inheritdoc />
    public Task PutBackDamagedDatabaseAsync(string libraryDirectory, string damagedPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(damagedPath);

        return Task.Run(
            () =>
            {
                var layout = LibraryLayout.FromDirectory(libraryDirectory);
                using var heldLock = LibraryLock.Acquire(layout, timeProvider.GetUtcNow());
                if (!File.Exists(damagedPath))
                {
                    throw new FileNotFoundException("The kept database is gone.", damagedPath);
                }

                // The restored database's WAL files would be replayed onto the old one.
                File.Delete(layout.DatabasePath + "-wal");
                File.Delete(layout.DatabasePath + "-shm");
                File.Move(damagedPath, layout.DatabasePath, overwrite: true);
                if (File.Exists(damagedPath + "-wal"))
                {
                    File.Move(damagedPath + "-wal", layout.DatabasePath + "-wal");
                }

                LogPutBack(layout.RootDirectory, damagedPath);
            },
            cancellationToken);
    }

    private string ReplaceDamagedDatabase(LibraryLayout layout, string backupPath)
    {
        using var heldLock = LibraryLock.Acquire(layout, timeProvider.GetUtcNow());
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("The backup is gone.", backupPath);
        }

        Directory.CreateDirectory(layout.BackupsDirectory);
        var damagedPath = Path.Combine(
            layout.BackupsDirectory,
            string.Create(CultureInfo.InvariantCulture, $"library-damaged-{timeProvider.GetUtcNow().ToString(BackupTimeFormat, CultureInfo.InvariantCulture)}.db"));
        if (File.Exists(damagedPath) || File.Exists(damagedPath + "-wal"))
        {
            throw new IOException($"The file {damagedPath} already exists.");
        }

        // The copy comes first, next to the database, so a full disk fails before anything is moved, and the last
        // step is a rename on the same volume.
        var incoming = layout.DatabasePath + ".restoring";
        File.Copy(backupPath, incoming, overwrite: true);
        var moved = new List<(string From, string To)>();
        try
        {
            MoveFile(layout.DatabasePath, damagedPath, moved);
            if (File.Exists(layout.DatabasePath + "-wal"))
            {
                MoveFile(layout.DatabasePath + "-wal", damagedPath + "-wal", moved);
            }

            // The shared-memory index only caches the WAL; SQLite rebuilds it.
            File.Delete(layout.DatabasePath + "-shm");
            File.Move(incoming, layout.DatabasePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Undo(layout, incoming, moved);
            throw;
        }

        LogReplaced(layout.RootDirectory, backupPath, damagedPath);
        return damagedPath;
    }

    private static void MoveFile(string from, string to, List<(string From, string To)> moved)
    {
        File.Move(from, to);
        moved.Add((from, to));
    }

    private void Undo(LibraryLayout layout, string incoming, List<(string From, string To)> moved)
    {
        // Each step on its own, so one that fails doesn't keep the others from putting their file back.
        TryUndo(layout, () => File.Delete(incoming), incoming);
        for (var i = moved.Count - 1; i >= 0; i--)
        {
            var (from, to) = moved[i];
            TryUndo(layout, () => File.Move(to, from), to);
        }
    }

    private void TryUndo(LibraryLayout layout, Action step, string path)
    {
        try
        {
            step();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogUndoFailed(exception, layout.RootDirectory, path);
        }
    }

    [GeneratedRegex(@"^library-v(?<version>\d{1,9})-(?<time>\d{8}-\d{6}-\d{3})\.db$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MigrationBackupName();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restored library {Directory} from {BackupPath}; the damaged database is kept as {DamagedPath}")]
    private partial void LogReplaced(string directory, string backupPath, string damagedPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Put the damaged database {DamagedPath} back into library {Directory}")]
    private partial void LogPutBack(string directory, string damagedPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't undo a step of a failed restore of library {Directory}: {Path}")]
    private partial void LogUndoFailed(Exception exception, string directory, string path);
}
