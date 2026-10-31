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

        Directory.CreateDirectory(layout.RootDirectory);
        Directory.CreateDirectory(layout.ThumbnailsDirectory);
        var now = timeProvider.GetUtcNow();
        var heldLock = LibraryLock.Acquire(layout, now);
        var connections = new LibraryConnectionFactory(layout.DatabasePath);
        try
        {
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
            Release(connections, heldLock);
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

    private static void Release(LibraryConnectionFactory connections, LibraryLock heldLock)
    {
        using (var connection = new SqliteConnection(connections.ConnectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        heldLock.Dispose();
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

        if (applied.Count == known.Count)
        {
            return;
        }

        var backupPath = Path.Combine(
            layout.BackupsDirectory,
            string.Create(CultureInfo.InvariantCulture, $"library-v{applied.Count}-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}.db"));
        await SqliteBackup.VacuumIntoAsync((SqliteConnection)db.Database.GetDbConnection(), backupPath, cancellationToken)
            .ConfigureAwait(false);
        LogBackedUp(layout.RootDirectory, applied.Count, backupPath);

        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlAsync($"UPDATE library SET schema_version = {known.Count}", cancellationToken)
            .ConfigureAwait(false);
        LogMigrated(layout.RootDirectory, applied.Count, known.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created library {Directory}")]
    private partial void LogCreated(string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backed up library {Directory} at schema version {Version} to {BackupPath}")]
    private partial void LogBackedUp(string directory, int version, string backupPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated library {Directory} from schema version {From} to {To}")]
    private partial void LogMigrated(string directory, int from, int to);
}
