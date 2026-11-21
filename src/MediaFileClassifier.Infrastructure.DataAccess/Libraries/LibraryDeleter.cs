using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// Deletes a library's own files (E02-S07-T01), as listed by <see cref="LibraryFiles"/>, while holding its lock. Every
/// delete is of one named file or of an empty folder, never recursive, so nothing the app didn't write can go, and links
/// to other folders aren't followed. The database goes first: if it can't be deleted, nothing is.
/// </summary>
/// <param name="timeProvider">Clock, for the lock file.</param>
/// <param name="logger">Logger.</param>
internal sealed partial class LibraryDeleter(TimeProvider timeProvider, ILogger<LibraryDeleter> logger) : ILibraryDeleter
{
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

    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static void EnsureExists(LibraryLayout layout)
    {
        if (!File.Exists(layout.DatabasePath))
        {
            throw new LibraryOpenException(LibraryOpenError.NotFound, layout.RootDirectory);
        }
    }

    private LibraryDeletionPreview Inspect(LibraryLayout layout)
    {
        EnsureExists(layout);
        using var heldLock = LibraryLock.Acquire(layout, timeProvider.GetUtcNow());

        // Plain SQL on tables every schema version has, so the library isn't migrated by looking. Read-write only so that
        // SQLite can read a WAL a crash left behind; nothing is written.
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
            var count = Convert.ToInt32(countCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            return new LibraryDeletionPreview(name, count);
        }
        catch (SqliteException exception)
        {
            throw new LibraryOpenException(LibraryOpenError.NotALibrary, layout.RootDirectory, exception);
        }
    }

    private LibraryDeletionResult Delete(LibraryLayout layout)
    {
        EnsureExists(layout);
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
        var removed = kept.Count == 0 && TryDeleteEmptyDirectory(layout.RootDirectory) == 0;
        if (failed > 0)
        {
            LogSomeNotDeleted(layout.RootDirectory, failed);
        }

        LogDeleted(layout.RootDirectory, removed, kept.Count);
        return new LibraryDeletionResult(removed, removed ? [] : kept);
    }

    private int DeleteThumbnails(string thumbnails)
    {
        if (!Directory.Exists(thumbnails) || IsLink(thumbnails))
        {
            return 0;
        }

        var failed = 0;
        foreach (var shard in Directory.EnumerateDirectories(thumbnails).ToList())
        {
            if (LibraryFiles.IsThumbnailShard(Path.GetFileName(shard)))
            {
                failed += DeleteMatching(shard, LibraryFiles.IsThumbnailFile);
            }
        }

        return failed + TryDeleteEmptyDirectory(thumbnails);
    }

    /// <summary>
    /// Deletes the matching files directly in a folder, then the folder if it's empty. A link to another folder is left
    /// alone, so its target is never looked into.
    /// </summary>
    private int DeleteMatching(string directory, Func<string, bool> isKnown)
    {
        if (!Directory.Exists(directory) || IsLink(directory))
        {
            return 0;
        }

        var failed = 0;
        foreach (var file in Directory.EnumerateFiles(directory).ToList())
        {
            if (isKnown(Path.GetFileName(file)))
            {
                failed += TryDeleteFile(file);
            }
        }

        return failed + TryDeleteEmptyDirectory(directory);
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

    private static List<string> ListEntries(string directory)
    {
        try
        {
            return [.. Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting library {Directory}: its database is deleted")]
    private partial void LogDatabaseDeleted(string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted library {Directory}; folder removed: {Removed}, entries kept: {Kept}")]
    private partial void LogDeleted(string directory, bool removed, int kept);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't delete {Path}")]
    private partial void LogFileNotDeleted(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting library {Directory} left {Count} of its own files or folders")]
    private partial void LogSomeNotDeleted(string directory, int count);
}
