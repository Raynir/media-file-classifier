using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.Domain.Media;

/// <summary>
/// One media file in a library. Its location is a folder plus a path relative to it, so the item survives
/// the root folder moving, and the same model works for Android folders.
/// </summary>
/// <remarks>
/// Invariants: the size isn't negative, and status changes follow <see cref="MediaItemStatusTransitions"/>.
/// Technical metadata, descriptive metadata, tags, analysis runs and embeddings reference the item by ID.
/// </remarks>
public sealed class MediaItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MediaItem"/> class from stored values.
    /// </summary>
    /// <param name="id">Item ID.</param>
    /// <param name="folderId">Indexed folder the file is in.</param>
    /// <param name="path">Path relative to the folder.</param>
    /// <param name="mediaType">Kind of media.</param>
    /// <param name="sizeBytes">File size in bytes.</param>
    /// <param name="fileCreatedAt">File creation time reported by the file system.</param>
    /// <param name="fileModifiedAt">File modification time reported by the file system.</param>
    /// <param name="importedAt">When the item was added to the library.</param>
    /// <param name="hashes">Content hashes computed so far.</param>
    /// <param name="status">Processing status.</param>
    /// <param name="statusReason">Error message or other reason for the status, if any.</param>
    /// <param name="statusChangedAt">When the status last changed.</param>
    /// <param name="reviewStatus">Whether the user reviewed the AI results (D021).</param>
    /// <param name="reviewStatusChangedAt">When the review status last changed, or null if it never did.</param>
    /// <param name="isFavorite">Whether the user marked the item as a favorite.</param>
    /// <param name="favoriteChangedAt">When the favorite mark last changed, or null if it never did.</param>
    public MediaItem(
        Id<MediaItem> id,
        Id<IndexedFolder> folderId,
        RelativePath path,
        MediaType mediaType,
        long sizeBytes,
        DateTimeOffset fileCreatedAt,
        DateTimeOffset fileModifiedAt,
        DateTimeOffset importedAt,
        FileHashes hashes,
        MediaItemStatus status,
        string? statusReason,
        DateTimeOffset statusChangedAt,
        ReviewStatus reviewStatus,
        DateTimeOffset? reviewStatusChangedAt,
        bool isFavorite,
        DateTimeOffset? favoriteChangedAt)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        ArgumentNullException.ThrowIfNull(hashes);

        Id = id;
        FolderId = folderId;
        Path = path;
        MediaType = mediaType;
        SizeBytes = sizeBytes;
        FileCreatedAt = fileCreatedAt;
        FileModifiedAt = fileModifiedAt;
        ImportedAt = importedAt;
        Hashes = hashes;
        Status = status;
        StatusReason = statusReason;
        StatusChangedAt = statusChangedAt;
        ReviewStatus = reviewStatus;
        ReviewStatusChangedAt = reviewStatusChangedAt;
        IsFavorite = isFavorite;
        FavoriteChangedAt = favoriteChangedAt;
    }

    /// <summary>
    /// Creates an empty instance for EF Core to fill when it loads the item (D071). EF Core can't pass the
    /// complex-typed values to the public constructor.
    /// </summary>
    private MediaItem()
    {
        Path = null!;
        Hashes = null!;
    }

    public Id<MediaItem> Id { get; }

    public Id<IndexedFolder> FolderId { get; private set; }

    public RelativePath Path { get; private set; }

    public string FileName => Path.FileName;

    /// <summary>
    /// Gets the file format, as the lower-case extension without the dot (spec 1.7 "file format" filter).
    /// </summary>
    public string FileFormat => Path.Extension;

    public MediaType MediaType { get; }

    public long SizeBytes { get; private set; }

    public DateTimeOffset FileCreatedAt { get; private set; }

    public DateTimeOffset FileModifiedAt { get; private set; }

    /// <summary>
    /// Gets when the item was added to the library, for the "import date" sort.
    /// </summary>
    public DateTimeOffset ImportedAt { get; }

    public FileHashes Hashes { get; private set; }

    public MediaItemStatus Status { get; private set; }

    public string? StatusReason { get; private set; }

    public DateTimeOffset StatusChangedAt { get; private set; }

    /// <summary>
    /// Gets the failed stage and the kind of failure while the status is <see cref="MediaItemStatus.Error"/> and the
    /// failure came from processing (E03-S07-T02); otherwise <c>null</c>.
    /// </summary>
    public MediaItemError? Error { get; private set; }

    public ReviewStatus ReviewStatus { get; private set; }

    /// <summary>
    /// Gets when the review status last changed, or null if it never changed since the item was found. A
    /// library package merge keeps the newer change (D076); null counts as older than any time.
    /// </summary>
    public DateTimeOffset? ReviewStatusChangedAt { get; private set; }

    public bool IsFavorite { get; private set; }

    /// <summary>
    /// Gets when the favorite mark last changed, or null if it never changed since the item was found. A
    /// library package merge keeps the newer change (D076); null counts as older than any time.
    /// </summary>
    public DateTimeOffset? FavoriteChangedAt { get; private set; }

    /// <summary>
    /// Creates an item for a newly found file, in <see cref="MediaItemStatus.Discovered"/> status.
    /// </summary>
    /// <param name="folderId">Indexed folder the file is in.</param>
    /// <param name="path">Path relative to the folder.</param>
    /// <param name="mediaType">Kind of media.</param>
    /// <param name="sizeBytes">File size in bytes.</param>
    /// <param name="fileCreatedAt">File creation time.</param>
    /// <param name="fileModifiedAt">File modification time.</param>
    /// <param name="fingerprint">Quick fingerprint, if computed during discovery.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The new item.</returns>
    public static MediaItem Discover(
        Id<IndexedFolder> folderId,
        RelativePath path,
        MediaType mediaType,
        long sizeBytes,
        DateTimeOffset fileCreatedAt,
        DateTimeOffset fileModifiedAt,
        UInt128? fingerprint,
        DateTimeOffset now) =>
        new(
            Id<MediaItem>.New(),
            folderId,
            path,
            mediaType,
            sizeBytes,
            fileCreatedAt,
            fileModifiedAt,
            now,
            new FileHashes { Fingerprint = fingerprint },
            MediaItemStatus.Discovered,
            null,
            now,
            ReviewStatus.Unreviewed,
            null,
            false,
            null);

    /// <summary>
    /// Changes the processing status.
    /// </summary>
    /// <param name="status">New status.</param>
    /// <param name="now">Current time.</param>
    /// <param name="reason">Error message or other reason, if any.</param>
    /// <exception cref="InvalidOperationException">The change isn't allowed by <see cref="MediaItemStatusTransitions"/>.</exception>
    public void ChangeStatus(MediaItemStatus status, DateTimeOffset now, string? reason = null)
    {
        if (!MediaItemStatusTransitions.IsAllowed(Status, status))
        {
            throw new InvalidOperationException($"Media item status can't change from {Status} to {status}.");
        }

        if (Status != status)
        {
            StatusChangedAt = now;
        }

        Status = status;
        StatusReason = reason;
        Error = null;
    }

    /// <summary>
    /// Marks the item as failed in a processing stage.
    /// </summary>
    /// <param name="error">The stage that failed and the kind of failure.</param>
    /// <param name="message">What went wrong, shown to the user.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="InvalidOperationException">The status can't change to <see cref="MediaItemStatus.Error"/>.</exception>
    public void MarkFailed(MediaItemError error, string message, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(error);

        ChangeStatus(MediaItemStatus.Error, now, message);
        Error = error;
    }

    /// <summary>
    /// Records that the file was moved or renamed. The item keeps its ID and all its metadata.
    /// </summary>
    /// <param name="folderId">Folder the file is in now.</param>
    /// <param name="path">New path relative to that folder.</param>
    public void Relocate(Id<IndexedFolder> folderId, RelativePath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        FolderId = folderId;
        Path = path;
    }

    /// <summary>
    /// Records new file-system facts after the file changed on disk. Hashes are replaced, because they describe the old content.
    /// </summary>
    /// <param name="sizeBytes">File size in bytes.</param>
    /// <param name="fileCreatedAt">File creation time.</param>
    /// <param name="fileModifiedAt">File modification time.</param>
    /// <param name="hashes">Hashes of the new content computed so far.</param>
    public void UpdateFile(long sizeBytes, DateTimeOffset fileCreatedAt, DateTimeOffset fileModifiedAt, FileHashes hashes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        ArgumentNullException.ThrowIfNull(hashes);

        SizeBytes = sizeBytes;
        FileCreatedAt = fileCreatedAt;
        FileModifiedAt = fileModifiedAt;
        Hashes = hashes;
    }

    /// <summary>
    /// Stores hashes computed later, e.g. the background full content hash.
    /// </summary>
    /// <param name="hashes">All hashes known for the current content.</param>
    public void SetHashes(FileHashes hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);

        Hashes = hashes;
    }

    /// <summary>
    /// Records that the user reviewed the AI results (D021).
    /// </summary>
    /// <param name="now">Current time.</param>
    public void MarkReviewed(DateTimeOffset now) => SetReviewStatus(ReviewStatus.Reviewed, now);

    /// <summary>
    /// Sends the item back to review, e.g. when a new analysis run changed its effective values (D021).
    /// </summary>
    /// <param name="now">Current time.</param>
    public void MarkUnreviewed(DateTimeOffset now) => SetReviewStatus(ReviewStatus.Unreviewed, now);

    /// <summary>
    /// Marks or unmarks the item as a favorite. The change time stays the same when the value does.
    /// </summary>
    /// <param name="isFavorite">Whether the item is a favorite.</param>
    /// <param name="now">Current time.</param>
    public void SetFavorite(bool isFavorite, DateTimeOffset now)
    {
        if (IsFavorite != isFavorite)
        {
            FavoriteChangedAt = now;
        }

        IsFavorite = isFavorite;
    }

    private void SetReviewStatus(ReviewStatus reviewStatus, DateTimeOffset now)
    {
        if (ReviewStatus != reviewStatus)
        {
            ReviewStatusChangedAt = now;
        }

        ReviewStatus = reviewStatus;
    }
}
