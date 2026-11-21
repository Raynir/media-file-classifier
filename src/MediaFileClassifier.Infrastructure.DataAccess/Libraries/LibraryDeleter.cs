using System.Globalization;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// Deletes a library's own files (E02-S07-T01), as listed by <see cref="LibraryFiles"/>, while holding its lock. Every
/// delete is of one named file or of an empty folder, never recursive, so nothing the app didn't write can go, and links
/// to other folders aren't followed. The database is checked to be a library and goes first: if it can't be deleted,
/// nothing is.
/// </summary>
/// <param name="timeProvider">Clock, for the lock file.</param>
/// <param name="logger">Logger.</param>
internal sealed partial class LibraryDeleter(TimeProvider timeProvider, ILogger<LibraryDeleter> logger) : ILibraryDeleter
{
    private const int SqliteError = 1;
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteNotADatabase = 26;

    /// <inheritdoc />
    public Task<LibraryDeletionPreview> InspectAsync(string libraryDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryDirectory);
        return Task.Run(() => Inspect(LibraryLayout.FromDirectory(libraryDirectory)), cancellationToken);
    }

    /// <inheritdoc />
    public Task<LibraryDeletionResult> DeleteAsync(string libraryDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryDirectory);
        return Task.Run(() => Delete(LibraryLayout.FromDirectory(libraryDirectory)), cancellationToken);
    }

    /// <summary>
    /// Reads the library's name and item count with plain SQL on tables every schema version has, so the library isn't
    /// migrated by looking. This also proves the database is a library before anything is deleted. Read-write only so
    /// that SQLite can read a WAL a crash left behind; nothing is written.
    /// </summary>
    private static LibraryDeletionPreview Read(LibraryLayout layout)
    {
        if (!File.Exists(layout.DatabasePath))
        {
            throw new LibraryOpenException(LibraryOpenError.NotFound, layout.RootDirectory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = layout.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();
        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var nameCommand = connection.CreateCommand();
            nameCommand.CommandText = "SELECT name FROM library LIMIT 1";
            var name = nameCommand.ExecuteScalar() as string
                ?? throw new LibraryOpenException(LibraryOpenError.NotALibrary, layout.RootDirectory);
            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT count(*) FROM media_item";
            var count = Convert.ToInt32(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            return new LibraryDeletionPreview(name, count);
        }
        catch (SqliteException exception)
        {
            // SQLITE_ERROR here is a missing table: another app's database.
            var error = exception.SqliteErrorCode switch
            {
                SqliteError or SqliteNotADatabase => LibraryOpenError.NotALibrary,
                SqliteBusy or SqliteLocked => LibraryOpenError.InUse,
                _ => LibraryOpenError.NotWritable,
            };
            throw new LibraryOpenException(error, layout.RootDirectory, exception);
        }
    }

    private LibraryDeletionPreview Inspect(LibraryLayout layout)
    {
        // Read before the lock, so a folder that isn't a library doesn't get a lock file.
        var preview = Read(layout);
        using var heldLock = LibraryLock.Acquire(layout, timeProvider.GetUtcNow());
        return preview;
    }

    private LibraryDeletionResult Delete(LibraryLayout layout)
    {
        Read(layout);
        var failed = 0;
        using (LibraryLock.Acquire(layout, timeProvider.GetUtcNow()))
        {
            try
            {
                File.Delete(layout.DatabasePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Still a whole library: give up before anything else goes.
                throw new LibraryOpenException(LibraryOpenError.NotWritable, layout.RootDirectory, exception);
            }

            LogDatabaseDeleted(layout.RootDirectory);
            foreach (var name in LibraryFiles.DatabaseFileNames.Skip(1))
            {
                failed += TryDeleteFile(Path.Combine(layout.RootDirectory, name));
            }

            failed += DeleteThumbnails(layout.ThumbnailsDirectory);
            failed += DeleteMatching(layout.BackupsDirectory, LibraryFiles.IsBackupFile);
        }

        // Released above; another instance may have taken it since, and then its file stays.
        failed += TryDeleteFile(layout.LockFilePath);
        var kept = ListEntries(layout.RootDirectory);
        var removed = kept is { Count: 0 } && TryDeleteEmptyDirectory(layout.RootDirectory) == 0 && !Directory.Exists(layout.RootDirectory);
        if (failed > 0)
        {
            LogSomeNotDeleted(layout.RootDirectory, failed);
        }

        LogDeleted(layout.RootDirectory, removed, kept?.Count ?? -1);
        return new LibraryDeletionResult(removed, removed ? [] : kept ?? []);
    }

    private int DeleteThumbnails(string thumbnails)
    {
        if (ListFolder(thumbnails, Directory.EnumerateDirectories) is not { } shards)
        {
            return Directory.Exists(thumbnails) ? 1 : 0;
        }

        var failed = 0;
        foreach (var shard in shards)
        {
            if (LibraryFiles.IsThumbnailShard(Path.GetFileName(shard)))
            {
                failed += DeleteMatching(shard, LibraryFiles.IsThumbnailFile);
            }
        }

        return failed + TryDeleteEmptyDirectory(thumbnails);
    }

    /// <summary>
    /// Deletes the matching files directly in a folder, then the folder if it's empty.
    /// </summary>
    private int DeleteMatching(string directory, Func<string, bool> isKnown)
    {
        if (ListFolder(directory, Directory.EnumerateFiles) is not { } files)
        {
            return Directory.Exists(directory) ? 1 : 0;
        }

        var failed = 0;
        foreach (var file in files)
        {
            if (isKnown(Path.GetFileName(file)))
            {
                failed += TryDeleteFile(file);
            }
        }

        return failed + TryDeleteEmptyDirectory(directory);
    }

    /// <summary>
    /// Lists a folder's files or subfolders. A link to another folder isn't looked into, so its target is never touched.
    /// </summary>
    /// <returns>The entries, or <c>null</c> when the folder is missing, a link, or can't be listed.</returns>
    private List<string>? ListFolder(string directory, Func<string, IEnumerable<string>> enumerate)
    {
        try
        {
            if (!Directory.Exists(directory) || File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }

            return [.. enumerate(directory)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFileNotDeleted(exception, directory);
            return null;
        }
    }

    private int TryDeleteFile(string path)
    {
        try
        {
            // File.Delete does nothing for a file that isn't there.
            File.Delete(path);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFileNotDeleted(exception, path);
            return 1;
        }
    }

    // Not recursive: a folder with anything left in it stays, and that isn't a failure.
    private int TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFileNotDeleted(exception, path);
            return 1;
        }
    }

    // Null when the folder can't be listed: it then stays, with nothing named as kept.
    private List<string>? ListEntries(string directory)
    {
        try
        {
            return [.. Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFileNotDeleted(exception, directory);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting library {Directory}: its database is deleted")]
    private partial void LogDatabaseDeleted(string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted library {Directory}; folder removed: {Removed}, entries kept: {Kept}")]
    private partial void LogDeleted(string directory, bool removed, int kept);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't delete or list {Path}")]
    private partial void LogFileNotDeleted(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting library {Directory} left {Count} of its own files or folders")]
    private partial void LogSomeNotDeleted(string directory, int count);
}
