using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// Online backup with <c>VACUUM INTO</c> (D073). It reads one consistent snapshot, so writes on other connections can
/// continue meanwhile; the copy is compacted, in rollback-journal mode, and needs no -wal file.
/// </summary>
internal static class SqliteBackup
{
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    /// <summary>
    /// Tells whether SQLite failed because the database file is damaged (<c>SQLITE_CORRUPT</c> or <c>SQLITE_NOTADB</c>).
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns><c>true</c> when the file is damaged.</returns>
    public static bool IsDamaged(SqliteException exception) =>
        exception.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase;

    /// <summary>
    /// Copies the database of a connection to a new file.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="destinationPath">Path of the copy, which must not exist yet. Its folder is created.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the copy is written.</returns>
    /// <exception cref="IOException">The destination exists.</exception>
    public static async Task VacuumIntoAsync(SqliteConnection connection, string destinationPath, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        if (File.Exists(fullPath))
        {
            throw new IOException($"The backup file {fullPath} already exists.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $destination";
        command.Parameters.AddWithValue("$destination", fullPath);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <c>PRAGMA integrity_check</c> on the database of a connection.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The problems found; empty if the database is intact.</returns>
    public static async Task<IReadOnlyList<string>> CheckIntegrityAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        var messages = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            messages.Add(reader.GetString(0));
        }

        return messages is ["ok"] ? [] : messages;
    }
}
