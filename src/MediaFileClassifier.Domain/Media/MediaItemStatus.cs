namespace MediaFileClassifier.Domain.Media;

/// <summary>
/// Processing status of a media item. <see cref="MediaItemStatusTransitions"/> lists the allowed changes.
/// </summary>
public enum MediaItemStatus
{
    /// <summary>
    /// The file was found and has a quick fingerprint, but isn't queued yet.
    /// </summary>
    Discovered,

    /// <summary>
    /// The item is queued for metadata extraction, hashing and analysis.
    /// </summary>
    Pending,

    /// <summary>
    /// Processing finished and the item is searchable.
    /// </summary>
    Indexed,

    /// <summary>
    /// Processing failed. The reason is kept in <see cref="MediaItem.StatusReason"/>.
    /// </summary>
    Error,

    /// <summary>
    /// The file is no longer at its path. Its metadata is kept, so the item can be matched again if the file comes back.
    /// </summary>
    Missing,

    /// <summary>
    /// The file is excluded by a folder rule or by the user. Its metadata is kept.
    /// </summary>
    Excluded,
}
