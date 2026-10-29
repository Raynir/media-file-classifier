namespace MediaFileClassifier.Domain.Media;

/// <summary>
/// Allowed changes of <see cref="MediaItemStatus"/>. Changing to the current status is always allowed, e.g. to update an error reason.
/// </summary>
/// <remarks>
/// <code>
/// Discovered → Pending | Error | Missing | Excluded
/// Pending    → Indexed | Error | Missing | Excluded
/// Indexed    → Pending | Error | Missing | Excluded   (Pending: file changed or re-index requested)
/// Error      → Pending | Missing | Excluded            (Pending: retry)
/// Missing    → Pending | Indexed | Excluded            (Indexed: file found again unchanged)
/// Excluded   → Pending | Missing                       (Pending: rule removed)
/// </code>
/// Nothing returns to <see cref="MediaItemStatus.Discovered"/>.
/// </remarks>
public static class MediaItemStatusTransitions
{
    private static readonly Dictionary<MediaItemStatus, MediaItemStatus[]> Allowed = new()
    {
        [MediaItemStatus.Discovered] = [MediaItemStatus.Pending, MediaItemStatus.Error, MediaItemStatus.Missing, MediaItemStatus.Excluded],
        [MediaItemStatus.Pending] = [MediaItemStatus.Indexed, MediaItemStatus.Error, MediaItemStatus.Missing, MediaItemStatus.Excluded],
        [MediaItemStatus.Indexed] = [MediaItemStatus.Pending, MediaItemStatus.Error, MediaItemStatus.Missing, MediaItemStatus.Excluded],
        [MediaItemStatus.Error] = [MediaItemStatus.Pending, MediaItemStatus.Missing, MediaItemStatus.Excluded],
        [MediaItemStatus.Missing] = [MediaItemStatus.Pending, MediaItemStatus.Indexed, MediaItemStatus.Excluded],
        [MediaItemStatus.Excluded] = [MediaItemStatus.Pending, MediaItemStatus.Missing],
    };

    public static bool IsAllowed(MediaItemStatus from, MediaItemStatus to) =>
        from == to || (Allowed.TryGetValue(from, out var targets) && targets.Contains(to));
}
