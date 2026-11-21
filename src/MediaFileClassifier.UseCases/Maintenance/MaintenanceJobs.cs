using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.UseCases.Maintenance;

/// <summary>
/// Job and stage keys of the library maintenance actions (E02-S06).
/// </summary>
public static class MaintenanceJobs
{
    /// <summary>
    /// The job type of Regenerate thumbnails.
    /// </summary>
    public const string ThumbnailsJob = "thumbnails";

    /// <summary>
    /// The stage that makes one item's missing or outdated thumbnails.
    /// </summary>
    public const string ThumbnailsStage = "thumbnails.regenerate";

    /// <summary>
    /// Tells whether an item has thumbnails to keep: it's imported and its file is still in the library's folders.
    /// Discovered items get theirs from the import, and missing or excluded ones show none.
    /// </summary>
    /// <param name="status">The item's status.</param>
    /// <returns>Whether the item's thumbnails are regenerated.</returns>
    public static bool HasThumbnails(MediaItemStatus status) =>
        status is MediaItemStatus.Pending or MediaItemStatus.Indexed or MediaItemStatus.Error;
}
