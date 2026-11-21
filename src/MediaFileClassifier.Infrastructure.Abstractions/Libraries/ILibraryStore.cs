namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// Creates and opens libraries (E01-S03).
/// </summary>
public interface ILibraryStore
{
    /// <summary>
    /// Creates the library folder <c>&lt;parent&gt;\&lt;name&gt;.mfc</c> with its database at the latest schema and
    /// its thumbnail cache, then opens it.
    /// </summary>
    /// <param name="parentDirectory">Folder the library folder goes in.</param>
    /// <param name="name">Library name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open library. The caller disposes it to close the library.</returns>
    /// <exception cref="LibraryOpenException">The folder already exists.</exception>
    Task<IOpenLibrary> CreateAsync(string parentDirectory, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a library. An older schema is migrated after the database is backed up.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open library. The caller disposes it to close the library.</returns>
    /// <exception cref="LibraryOpenException">The library is missing, in use, or newer than the app.</exception>
    Task<IOpenLibrary> OpenAsync(string libraryDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a library, reporting when a schema migration starts, so the UI can show its progress (E02-S02-T05).
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <param name="progress">Receives <see cref="LibraryOpenStage"/> changes, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open library. The caller disposes it to close the library.</returns>
    /// <exception cref="LibraryOpenException">The library is missing, in use, newer than the app, or its migration failed.</exception>
    Task<IOpenLibrary> OpenAsync(string libraryDirectory, IProgress<LibraryOpenStage>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a database backup back in place of a library's database, after a failed migration.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <param name="backupPath">The backup, from <see cref="LibraryOpenException.BackupPath"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the backup is restored.</returns>
    /// <exception cref="LibraryOpenException">The library is in use.</exception>
    Task RestoreBackupAsync(string libraryDirectory, string backupPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the schema-migration backups of a library, newest first (E02-S06-T03). The damaged databases kept by
    /// <see cref="ReplaceDamagedDatabaseAsync"/> aren't listed.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <returns>The backups; empty when there are none.</returns>
    IReadOnlyList<LibraryBackup> GetBackups(string libraryDirectory);

    /// <summary>
    /// Runs the integrity check on a backup, read-only, without opening it as a library.
    /// </summary>
    /// <param name="backupPath">The backup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result; a file SQLite can't read is a problem, not an exception.</returns>
    Task<IntegrityCheckResult> CheckBackupAsync(string backupPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a backup in place of a damaged database and keeps the damaged one (with its WAL file) in
    /// <c>backups/library-damaged-&lt;time&gt;.db</c> (E02-S06-T03). The library must be closed. When a step fails,
    /// the files are put back as they were.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <param name="backupPath">The backup, from <see cref="GetBackups"/>.</param>
    /// <param name="cancellationToken">Cancellation token; it's only checked before any file is touched.</param>
    /// <returns>The path the damaged database was moved to.</returns>
    /// <exception cref="LibraryOpenException">The library is in use.</exception>
    Task<string> ReplaceDamagedDatabaseAsync(string libraryDirectory, string backupPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes <see cref="ReplaceDamagedDatabaseAsync"/> when the restored library couldn't be opened: the kept database
    /// (and its WAL file) goes back in place. The library must be closed.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <param name="damagedPath">The path <see cref="ReplaceDamagedDatabaseAsync"/> returned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the database is back.</returns>
    /// <exception cref="LibraryOpenException">The library is in use.</exception>
    Task PutBackDamagedDatabaseAsync(string libraryDirectory, string damagedPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// What opening a library is doing.
/// </summary>
public enum LibraryOpenStage
{
    /// <summary>
    /// Backing up the database and updating it to the current schema.
    /// </summary>
    Migrating,
}
