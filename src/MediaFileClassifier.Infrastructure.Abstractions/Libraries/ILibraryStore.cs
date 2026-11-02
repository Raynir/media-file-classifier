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
