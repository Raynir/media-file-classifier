using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// Paths inside a library folder (E01-S03-T01). A library is a self-contained folder that can be backed up or moved
/// as a unit. See <c>docs/library-storage.md</c>.
/// <code>
/// &lt;name&gt;.mfc/
///   library.db        SQLite database; library.db-wal and library.db-shm appear while it's open (WAL mode)
///   library.lock      held open by the app instance that has the library open
///   thumbnails/xx/    thumbnail cache, &lt;item-id&gt;-256.webp and -512.webp; xx is the last two hex digits of the item ID
///   backups/          database copies, e.g. the one taken before a schema migration
/// </code>
/// </summary>
public sealed record LibraryLayout
{
    /// <summary>
    /// Extension of a library folder.
    /// </summary>
    public const string Extension = ".mfc";

    /// <summary>
    /// File name of the database.
    /// </summary>
    public const string DatabaseFileName = "library.db";

    /// <summary>
    /// File name of the lock file.
    /// </summary>
    public const string LockFileName = "library.lock";

    /// <summary>
    /// Folder name of the thumbnail cache.
    /// </summary>
    public const string ThumbnailsFolderName = "thumbnails";

    /// <summary>
    /// File extension of the thumbnails, which are WebP.
    /// </summary>
    public const string ThumbnailFileExtension = ".webp";

    /// <summary>
    /// Folder name of the database backups.
    /// </summary>
    public const string BackupsFolderName = "backups";

    private LibraryLayout(string rootDirectory) => RootDirectory = rootDirectory;

    /// <summary>
    /// Gets the library folder, e.g. <c>D:\Media\Family.mfc</c>.
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// Gets the full path of the database.
    /// </summary>
    public string DatabasePath => Path.Combine(RootDirectory, DatabaseFileName);

    /// <summary>
    /// Gets the full path of the lock file.
    /// </summary>
    public string LockFilePath => Path.Combine(RootDirectory, LockFileName);

    /// <summary>
    /// Gets the thumbnail cache folder.
    /// </summary>
    public string ThumbnailsDirectory => Path.Combine(RootDirectory, ThumbnailsFolderName);

    /// <summary>
    /// Gets the database backup folder.
    /// </summary>
    public string BackupsDirectory => Path.Combine(RootDirectory, BackupsFolderName);

    /// <summary>
    /// Gets the layout of an existing library folder.
    /// </summary>
    /// <param name="rootDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <returns>The layout.</returns>
    public static LibraryLayout FromDirectory(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        return new LibraryLayout(Path.GetFullPath(rootDirectory));
    }

    /// <summary>
    /// Gets the layout of a new library, <c>&lt;parent&gt;\&lt;name&gt;.mfc</c>.
    /// </summary>
    /// <param name="parentDirectory">Folder the library folder goes in.</param>
    /// <param name="name">Library name, which becomes the folder name.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentException">The name can't be a folder name.</exception>
    public static LibraryLayout For(string parentDirectory, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"'{name}' can't be used as a folder name.", nameof(name));
        }

        return FromDirectory(Path.Combine(parentDirectory, name.Trim() + Extension));
    }

    /// <summary>
    /// Gets the path of an item's thumbnail in one size (D078), e.g. <c>thumbnails/a7/&lt;item-id&gt;-256.webp</c>.
    /// Thumbnails are sharded into 256 folders by the last two hex digits of the item ID, which are random in a UUIDv7;
    /// its first digits are a timestamp and would cluster.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="size">The thumbnail size.</param>
    /// <returns>The full path of the thumbnail file.</returns>
    public string GetThumbnailPath(Id<MediaItem> itemId, ThumbnailSize size)
    {
        var id = itemId.Value.ToString();
        return Path.Combine(ThumbnailsDirectory, id[^2..], FormattableString.Invariant($"{id}-{(int)size}{ThumbnailFileExtension}"));
    }
}
