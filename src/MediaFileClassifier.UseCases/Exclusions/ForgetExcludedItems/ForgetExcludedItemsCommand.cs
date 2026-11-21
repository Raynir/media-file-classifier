using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Exclusions.ForgetExcludedItems;

/// <summary>
/// Forgets items the user removed from the library (E02-S10-T04): their records are deleted, with their user values and
/// collection memberships, and their thumbnails. The files stay on disk, so a file that's still in an indexed folder is
/// imported as a new item by the next scan.
/// </summary>
/// <param name="ItemIds">The items. Items that aren't removed by the user, e.g. restored meanwhile, are kept.</param>
public sealed record ForgetExcludedItemsCommand(IReadOnlyCollection<Id<MediaItem>> ItemIds) : IRequest<int>;

/// <summary>
/// Handles <see cref="ForgetExcludedItemsCommand"/>; returns the number of items forgotten.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="thumbnails">Deletes the thumbnails.</param>
/// <param name="logger">Logger.</param>
public sealed class ForgetExcludedItemsCommandHandler(
    ILibrarySession session,
    IThumbnailCache thumbnails,
    ILogger<ForgetExcludedItemsCommandHandler> logger) : IRequestHandler<ForgetExcludedItemsCommand, int>
{
    /// <inheritdoc />
    public async Task<int> Handle(ForgetExcludedItemsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        List<Id<MediaItem>> removed = [.. (await library.Items.GetByIdsAsync(request.ItemIds, cancellationToken).ConfigureAwait(false))
            .Where(LibraryExclusions.IsRemovedByUser)
            .Select(item => item.Id)];
        if (removed.Count == 0)
        {
            return 0;
        }

        // Thumbnails first: a record without them is harmless, thumbnails without a record would never be deleted.
        foreach (var itemId in removed)
        {
            thumbnails.Invalidate(library.Layout, itemId);
        }

        var deleted = await library.Items.DeleteExcludedAsync(removed, LibraryExclusions.RemovedByUser, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Forgot {Count} items removed from the library", deleted.Count);
        return deleted.Count;
    }
}
