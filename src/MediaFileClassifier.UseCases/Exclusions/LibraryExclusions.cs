using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.UseCases.Exclusions;

/// <summary>
/// Shared names of removing items from the library (E02-S10, D024): the item is marked Excluded with
/// <see cref="RemovedByUser"/> as its status reason, keeps its identity, user values and collections, and loses its AI
/// results, embeddings and thumbnails. The file is never touched.
/// </summary>
public static class LibraryExclusions
{
    /// <summary>
    /// The status reason of an item the user removed from the library, which tells it apart from an item its folder's
    /// rules excluded (<see cref="Libraries.IndexedFolders.ExcludedByRules"/>, D187).
    /// </summary>
    public const string RemovedByUser = "Removed from the library by the user.";

    /// <summary>
    /// The status reason of a removed item the user restored, while the import brings it back (E02-S10-T04).
    /// <see cref="Libraries.IndexedFolders.IsBackInRules"/> lets the import take it, like an item back in its folder's
    /// rules.
    /// </summary>
    public const string RestoreRequested = "Restore requested by the user.";

    /// <summary>
    /// Job type that deletes the AI results and thumbnails of a large selection after it was marked Excluded.
    /// </summary>
    public const string CleanupJob = "items-exclude";

    /// <summary>
    /// The stage of <see cref="CleanupJob"/>. Each work item carries a batch of item IDs as its payload.
    /// </summary>
    public const string CleanupStage = "exclusion.cleanup";

    /// <summary>
    /// The most items whose AI results are deleted before the remove command returns. Larger selections are cleaned
    /// up by a <see cref="CleanupJob"/>; they're Excluded, and so out of sight, either way.
    /// </summary>
    public const int InlineCleanupLimit = 200;

    /// <summary>
    /// Number of items per work item of <see cref="CleanupJob"/>, the writer's batch size (D072).
    /// </summary>
    public const int CleanupBatchSize = 100;

    /// <summary>
    /// Tells whether the user removed an item from the library, as opposed to its folder's rules excluding it.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> for an item Excluded with <see cref="RemovedByUser"/>.</returns>
    public static bool IsRemovedByUser(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Status == MediaItemStatus.Excluded && item.StatusReason == RemovedByUser;
    }

    /// <summary>
    /// Tells whether an item is listed in Excluded items: removed by the user, or restored by them but not imported yet,
    /// e.g. because its import failed (E02-S10-T04). Such an item can be restored or forgotten again.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> for an item Excluded with <see cref="RemovedByUser"/> or <see cref="RestoreRequested"/>.</returns>
    public static bool IsListedAsRemoved(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Status == MediaItemStatus.Excluded && item.StatusReason is RemovedByUser or RestoreRequested;
    }
}
