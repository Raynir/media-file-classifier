namespace MediaFileClassifier.Domain.Media;

/// <summary>
/// Whether the user has reviewed an item's AI results (D021, E08).
/// </summary>
public enum ReviewStatus
{
    /// <summary>
    /// Not reviewed yet, or a new analysis run changed the item's effective values since the last review.
    /// </summary>
    Unreviewed,

    /// <summary>
    /// The user accepted or edited the item in review mode.
    /// </summary>
    Reviewed,
}
