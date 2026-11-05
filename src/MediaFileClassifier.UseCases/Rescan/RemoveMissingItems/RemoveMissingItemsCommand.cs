using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Rescan.RemoveMissingItems;

/// <summary>
/// Removes every Missing item from the library (E02-S04-T04): the item with its metadata, tags, embeddings and
/// thumbnails goes, and nothing on disk is touched. An item whose file came back before the write stays.
/// </summary>
public sealed record RemoveMissingItemsCommand : IRequest<int>;

/// <summary>
/// Handles <see cref="RemoveMissingItemsCommand"/>, deleting in short batches so other writes get their turn.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="thumbnails">Deletes the thumbnails.</param>
/// <param name="logger">Logger.</param>
public sealed class RemoveMissingItemsCommandHandler(
    ILibrarySession session,
    IThumbnailCache thumbnails,
    ILogger<RemoveMissingItemsCommandHandler> logger) : IRequestHandler<RemoveMissingItemsCommand, int>
{
    /// <summary>
    /// Number of items deleted per write.
    /// </summary>
    public const int BatchSize = 500;

    /// <inheritdoc />
    /// <returns>The number of items removed.</returns>
    public async Task<int> Handle(RemoveMissingItemsCommand request, CancellationToken cancellationToken)
    {
        var library = session.Current ?? throw new InvalidOperationException("No library is open.");

        // Thumbnails first, as removing a folder does: an interrupted removal finds the items left and deletes their
        // thumbnails again, but a thumbnail whose item is gone would never be found. A thumbnail of an item that came
        // back meanwhile is only a cache and is made again.
        foreach (var item in await library.Items.GetByStatusAsync(MediaItemStatus.Missing, null, cancellationToken).ConfigureAwait(false))
        {
            thumbnails.Invalidate(library.Layout, item.Id);
        }

        var deleted = 0;
        while (true)
        {
            var batch = await library.Items.DeleteMissingAsync(BatchSize, cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            deleted += batch.Count;
        }

        logger.LogInformation("Removed {Count} missing items from the library", deleted);
        return deleted;
    }
}
