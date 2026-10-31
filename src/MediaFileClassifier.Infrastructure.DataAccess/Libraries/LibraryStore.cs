using System.Globalization;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// Creates and opens library folders (E01-S03). Opening an older library migrates its schema after a
/// <c>VACUUM INTO</c> backup into <c>backups/</c>. Both run on a thread-pool thread, because Microsoft.Data.Sqlite
/// runs its async methods synchronously.
/// </summary>
/// <param name="timeProvider">Clock.</param>
/// <param name="logger">Logger.</param>
internal sealed partial class LibraryStore(TimeProvider timeProvider, ILogger<LibraryStore> logger) : ILibraryStore
{
    private const int SqliteNotADatabase = 26;

    /// <inheritdoc />
    public Task<IOpenLibrary> CreateAsync(string parentDirectory, string name, CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateCoreAsync(parentDirectory, name, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IOpenLibrary> OpenAsync(string libraryDirectory, CancellationToken cancellationToken = default) =>
        Task.Run(() => OpenCoreAsync(libraryDirectory, cancellationToken), cancellationToken);

    private async Task<IOpenLibrary> CreateCoreAsync(string parentDirectory, string name, CancellationToken cancellationToken)
    {
        var layout = LibraryLayout.For(parentDirectory, name);
        if (Directory.Exists(layout.RootDirectory) && Directory.EnumerateFileSystemEntries(layout.RootDirectory).Any())
        {
            throw new LibraryOpenException(LibraryOpenError.AlreadyExists, layout.RootDirectory);
        }

        var createdRoot = !Directory.Exists(layout.RootDirectory);
        Directory.CreateDirectory(layout.RootDirectory);
        var now = timeProvider.GetUtcNow();
        var connections = new LibraryConnectionFactory(layout.DatabasePath);
        LibraryLock? heldLock = null;
        try
        {
            heldLock = LibraryLock.Acquire(layout, now);
            Directory.CreateDirectory(layout.ThumbnailsDirectory);
            Library library;
            await using (var db = await connections.CreateContextAsync(tracking: true, cancellationToken).ConfigureAwait(false))
            {
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                library = Library.Create(name, now);
                db.Libraries.Add(library);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            LogCreated(layout.RootDirectory);
            return await OpenLibrary.StartAsync(layout, library, connections, heldLock, timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Without the lock, another instance may be creating the same library; its files aren't ours to remove.
            var ours = heldLock is not null;
            Release(connections, heldLock);
            if (ours)
            {
                RemoveUnfinished(layout, createdRoot);
            }

            throw;
        }
    }

    private async Task<IOpenLibrary> OpenCoreAsync(string libraryDirectory, CancellationToken cancellationToken)
    {
        var layout = LibraryLayout.FromDirectory(libraryDirectory);
        if (!File.Exists(layout.DatabasePath))
        {
            throw new LibraryOpenException(LibraryOpenError.NotFound, layout.RootDirectory);
        }

        await EnsureLibraryDatabaseAsync(layout, cancellationToken).ConfigureAwait(false);
        var heldLock = LibraryLock.Acquire(layout, timeProvider.GetUtcNow());
        var connections = new LibraryConnectionFactory(layout.DatabasePath);
        try
        {
            Library library;
            await using (var db = await connections.CreateContextAsync(tracking: false, cancellationToken).ConfigureAwait(false))
            {
                await MigrateAsync(db, layout, cancellationToken).ConfigureAwait(false);
                library = await db.Libraries.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new LibraryOpenException(LibraryOpenError.NotALibrary, layout.RootDirectory);
            }

            Directory.CreateDirectory(layout.ThumbnailsDirectory);
            return await OpenLibrary.StartAsync(layout, library, connections, heldLock, timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == SqliteNotADatabase)
        {
            Release(connections, heldLock);
            throw new LibraryOpenException(LibraryOpenError.NotALibrary, layout.RootDirectory, exception);
        }
        catch
        {
            Release(connections, heldLock);
            throw;
        }
    }

    /// <summary>
    /// Checks that the database has a schema version table, before the lock file is created and before the connection
    /// pragmas run, so a database that isn't a library is left exactly as it was. <c>journal_mode = WAL</c> would
    /// otherwise be written into its header.
    /// </summary>
    private static async Task EnsureLibraryDatabaseAsync(LibraryLayout layout, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = layout.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();
        long tables;
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $table";
            command.Parameters.AddWithValue("$table", LibraryDbContext.SchemaVersionTable);
            tables = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == SqliteNotADatabase)
        {
            throw new LibraryOpenException(LibraryOpenError.NotALibrary, layout.RootDirectory, exception);
        }

        if (tables == 0)
        {
            throw new LibraryOpenException(LibraryOpenError.NotALibrary, layout.RootDirectory);
        }
    }

    private static void Release(LibraryConnectionFactory connections, LibraryLock? heldLock)
    {
        using (var connection = new SqliteConnection(connections.ConnectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        heldLock?.Dispose();
    }

    /// <summary>
    /// Removes what a failed or cancelled <see cref="CreateAsync"/> left, so the library can be created again. The
    /// folder was empty or missing before, so everything in it is ours.
    /// </summary>
    private void RemoveUnfinished(LibraryLayout layout, bool createdRoot)
    {
        try
        {
            if (createdRoot)
            {
                Directory.Delete(layout.RootDirectory, recursive: true);
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(layout.RootDirectory))
            {
                Directory.Delete(directory, recursive: true);
            }

            foreach (var file in Directory.EnumerateFiles(layout.RootDirectory))
            {
                File.Delete(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCleanupFailed(exception, layout.RootDirectory);
        }
    }

    private async Task MigrateAsync(LibraryDbContext db, LibraryLayout layout, CancellationToken cancellationToken)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var known = db.Database.GetMigrations().ToList();
        if (applied.Count == 0)
        {
            // Every library is created at a schema version, so a database without one isn't a library.
            throw new LibraryOpenException(LibraryOpenError.NotALibrary, layout.RootDirectory);
        }

        if (applied.Except(known).Any())
        {
            throw new LibraryOpenException(LibraryOpenError.NewerVersion, layout.RootDirectory);
        }

        if (applied.Count < known.Count)
        {
            // Milliseconds, so a migration retried right after a failed one doesn't collide with its backup.
            var backupPath = Path.Combine(
                layout.BackupsDirectory,
                string.Create(CultureInfo.InvariantCulture, $"library-v{applied.Count}-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss-fff}.db"));
            await SqliteBackup.VacuumIntoAsync((SqliteConnection)db.Database.GetDbConnection(), backupPath, cancellationToken)
                .ConfigureAwait(false);
            LogBackedUp(layout.RootDirectory, applied.Count, backupPath);

            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            LogMigrated(layout.RootDirectory, applied.Count, known.Count);
        }

        // On every open, not only after a migration: this also repairs a version left behind when the app stopped
        // between migrating and this update.
        await db.Database
            .ExecuteSqlAsync($"UPDATE library SET schema_version = {known.Count} WHERE schema_version <> {known.Count}", cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created library {Directory}")]
    private partial void LogCreated(string directory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't remove the unfinished library {Directory}")]
    private partial void LogCleanupFailed(Exception exception, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backed up library {Directory} at schema version {Version} to {BackupPath}")]
    private partial void LogBackedUp(string directory, int version, string backupPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated library {Directory} from schema version {From} to {To}")]
    private partial void LogMigrated(string directory, int from, int to);
}
