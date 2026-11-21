using System.Text.Json;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

namespace MediaFileClassifier.UseCases.Exclusions;

/// <summary>
/// The stage of <see cref="LibraryExclusions.CleanupJob"/> (E02-S10-T01): deletes the AI results, embeddings and
/// thumbnails of a batch of items the user removed from the library. Only items that are still removed are cleaned,
/// so a restore that came first keeps its data, and running a batch again does nothing more. Files are never touched.
/// </summary>
/// <param name="thumbnails">Deletes the thumbnails and keyframe pictures.</param>
public sealed class ExclusionCleanupStage(IThumbnailCache thumbnails) : IStageHandler
{
    /// <inheritdoc />
    public string Stage => LibraryExclusions.CleanupStage;

    /// <inheritdoc />
    public ResourcePool Pool => ResourcePool.Io;

    /// <summary>
    /// Writes the payload of a work item.
    /// </summary>
    /// <param name="itemIds">The batch of items.</param>
    /// <returns>The payload.</returns>
    public static string ToPayload(IEnumerable<Id<MediaItem>> itemIds) =>
        JsonSerializer.Serialize(itemIds.Select(id => id.ToString()));

    /// <summary>
    /// Reads the payload of a work item.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <returns>The batch of items.</returns>
    public static IReadOnlyList<Id<MediaItem>> FromPayload(string? payload) =>
        string.IsNullOrEmpty(payload)
            ? []
            : [.. (JsonSerializer.Deserialize<string[]>(payload) ?? []).Select(Id<MediaItem>.Parse)];

    /// <summary>
    /// Deletes the AI results, embeddings and thumbnails of the items that are still removed from the library.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="thumbnails">Deletes the thumbnails.</param>
    /// <param name="itemIds">The items.</param>
    /// <param name="cancellationToken">Cancellation token. Batches already written stay written.</param>
    /// <returns>The number of items cleaned up.</returns>
    public static async Task<int> CleanUpAsync(IOpenLibrary library, IThumbnailCache thumbnails, IReadOnlyCollection<Id<MediaItem>> itemIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(thumbnails);
        ArgumentNullException.ThrowIfNull(itemIds);

        List<Id<MediaItem>> removed = [.. (await library.Items.GetByIdsAsync(itemIds, cancellationToken).ConfigureAwait(false))
            .Where(LibraryExclusions.IsRemovedByUser)
            .Select(item => item.Id)];
        if (removed.Count == 0)
        {
            return 0;
        }

        foreach (var itemId in removed)
        {
            thumbnails.Invalidate(library.Layout, itemId);
        }

        await library.Embeddings.RemoveForItemsAsync(removed, cancellationToken).ConfigureAwait(false);
        await library.Metadata.RemoveAiResultsAsync(removed, cancellationToken).ConfigureAwait(false);
        return removed.Count;
    }

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await CleanUpAsync(context.Library, thumbnails, FromPayload(context.WorkItem.Payload), cancellationToken).ConfigureAwait(false);
    }
}
