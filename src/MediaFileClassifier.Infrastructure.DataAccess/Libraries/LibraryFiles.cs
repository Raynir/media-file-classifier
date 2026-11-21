using System.Text.RegularExpressions;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// The files the app writes into a library folder (D077, D078, E02-S06), by exact name, so deleting a library
/// (E02-S07) removes those and nothing else. A file the app starts writing under a new name must be added here.
/// <code>
/// &lt;name&gt;.mfc/
///   library.db, library.db-wal, library.db-shm, library.db-journal
///   library.lock
///   thumbnails/&lt;2 hex digits&gt;/&lt;item-id&gt;-&lt;size&gt;.webp, &lt;item-id&gt;-frame-&lt;n&gt;.webp, and either with .&lt;32 hex&gt;.tmp
///   backups/library-v&lt;n&gt;-&lt;time&gt;.db, library-damaged-&lt;time&gt;.db, and either with -wal, -shm or -journal
/// </code>
/// Names are compared ignoring case, as Windows does.
/// </summary>
internal static partial class LibraryFiles
{
    /// <summary>
    /// Gets the names of the library's own files directly in the library folder. The lock file isn't one of them: it's
    /// held while the rest are deleted, and goes last.
    /// </summary>
    public static IReadOnlyList<string> DatabaseFileNames { get; } =
    [
        LibraryLayout.DatabaseFileName,
        LibraryLayout.DatabaseFileName + "-wal",
        LibraryLayout.DatabaseFileName + "-shm",
        LibraryLayout.DatabaseFileName + "-journal",
    ];

    /// <summary>
    /// Checks whether a folder in <c>thumbnails/</c> is one of the app's shard folders.
    /// </summary>
    /// <param name="name">The folder name.</param>
    /// <returns><c>true</c> for two hex digits.</returns>
    public static bool IsThumbnailShard(string name) => ThumbnailShardName().IsMatch(name);

    /// <summary>
    /// Checks whether a file in a thumbnail shard folder is an item thumbnail, a keyframe picture, or the temporary file
    /// of either.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns><c>true</c> if the app wrote it.</returns>
    public static bool IsThumbnailFile(string name) => ThumbnailFileName().IsMatch(name);

    /// <summary>
    /// Checks whether a file in <c>backups/</c> is a schema-migration backup or a kept damaged database, or one of their
    /// SQLite side files.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns><c>true</c> if the app wrote it.</returns>
    public static bool IsBackupFile(string name) => BackupFileName().IsMatch(name);

    [GeneratedRegex(@"^[0-9a-f]{2}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ThumbnailShardName();

    [GeneratedRegex(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}-(\d{1,5}|frame-\d{1,9})\.webp(\.[0-9a-f]{32}\.tmp)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ThumbnailFileName();

    [GeneratedRegex(
        @"^library-(v\d{1,9}|damaged)-\d{8}-\d{6}-\d{3}\.db(-wal|-shm|-journal)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BackupFileName();
}
