using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Exclusions.ExcludeItems;

/// <summary>
/// Removes items from the library (E02-S10-T01, D024). The items are marked Excluded at once, so they leave search,
/// browsing, collections and statistics whatever the count. Their identity, user values and collection memberships
/// stay; their AI results, embeddings and thumbnails are deleted, before the command returns for a small selection and
/// by a background job for a large one. Files are never touched.
/// </summary>
/// <param name="ItemIds">The items.</param>
public sealed record ExcludeItemsCommand(IReadOnlyCollection<Id<MediaItem>> ItemIds) : IRequest<ExcludeItemsResult>;

/// <summary>
/// What removing the items did.
/// </summary>
/// <param name="Removed">Items marked Excluded; items already excluded aren't counted.</param>
/// <param name="CleanupQueued">Whether their AI results are deleted by a background job.</param>
public sealed record ExcludeItemsResult(int Removed, bool CleanupQueued);

/// <summary>
/// Handles <see cref="ExcludeItemsCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="thumbnails">Deletes the thumbnails of a small selection.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
/// <param name="jobs">Runs the cleanup of a large selection, if the platform has background processing.</param>
public sealed class ExcludeItemsCommandHandler(
    ILibrarySession session,
    IThumbnailCache thumbnails,
    TimeProvider clock,
    ILogger<ExcludeItemsCommandHandler> logger,
    IJobSystem? jobs = null) : IRequestHandler<ExcludeItemsCommand, ExcludeItemsResult>
{
    /// <inheritdoc />
    public async Task<ExcludeItemsResult> Handle(ExcludeItemsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        var removed = await library.Items.ExcludeAsync(request.ItemIds, LibraryExclusions.RemovedByUser, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (removed.Count == 0)
        {
            return new ExcludeItemsResult(0, false);
        }

        if (removed.Count <= LibraryExclusions.InlineCleanupLimit || jobs?.Current is not { } scheduler)
        {
            await ExclusionCleanupStage.CleanUpAsync(library, thumbnails, removed, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Removed {Count} items from the library", removed.Count);
            return new ExcludeItemsResult(removed.Count, false);
        }

        var work = removed.Chunk(LibraryExclusions.CleanupBatchSize)
            .Select(batch => new WorkItemRequest(LibraryExclusions.CleanupStage, Payload: ExclusionCleanupStage.ToPayload(batch)))
            .ToList();
        await scheduler.EnqueueJobAsync(
            new JobRequest(LibraryExclusions.CleanupJob, $"Remove {removed.Count} items from the library", JobPriority.UserAction, work),
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Removed {Count} items from the library; their AI results are deleted in the background", removed.Count);
        return new ExcludeItemsResult(removed.Count, true);
    }
}
