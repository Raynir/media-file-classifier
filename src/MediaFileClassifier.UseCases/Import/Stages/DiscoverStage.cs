using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Libraries;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Import.Stages;

/// <summary>
/// The first stage of an import: discovers the job's folder (E03-S02) and queues <see cref="ImportStages.Fingerprint"/>
/// for each batch of new items as soon as it's written, so the job's total grows while the scan goes on and items are
/// processed from the start.
/// </summary>
/// <remarks>
/// Resuming (E03-S07-T04): discovery never registers a known path twice, and when it's done, every item of the folder
/// still Discovered without work in this job is queued too. That covers a batch written just before an interruption,
/// and items left over from a cancelled import.
/// </remarks>
/// <param name="discovery">Scans the folder.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
public sealed partial class DiscoverStage(IFolderDiscovery discovery, TimeProvider clock, ILogger<DiscoverStage> logger) : IStageHandler
{
    /// <inheritdoc />
    public string Stage => ImportStages.Discover;

    /// <inheritdoc />
    public ResourcePool Pool => ResourcePool.Io;

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var library = context.Library;
        var jobId = context.WorkItem.JobId;
        var job = await library.Jobs.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        var folderId = job?.FolderId ?? throw new InvalidOperationException("An import job needs a folder.");
        var folder = await library.Folders.GetAsync(folderId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The import's folder isn't in the library any more.");

        DiscoveryResult result;
        try
        {
            result = await discovery.DiscoverAsync(
                library,
                folder,
                registered: (batch, token) => EnqueueAsync(context.Scheduler, jobId, [.. batch.Select(item => item.Id)], token),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException)
        {
            // E.g. its drive isn't connected: the folder shows as offline until it's back (E02-S03).
            folder.MarkOffline(clock.GetUtcNow());
            await library.Folders.UpdateAsync(folder, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (folder.Status == FolderStatus.Offline)
        {
            folder.MarkOnline(clock.GetUtcNow());
            await library.Folders.UpdateAsync(folder, cancellationToken).ConfigureAwait(false);
        }

        var queued = (await library.Jobs.GetWorkItemsAsync(jobId, cancellationToken).ConfigureAwait(false))
            .Where(workItem => workItem.ItemId is not null)
            .Select(workItem => workItem.ItemId!.Value)
            .ToHashSet();
        // Excluded items that the rules include again are imported again too (E02-S03-T03).
        var matcher = new FolderRuleMatcher(folder.Rules);
        var restored = (await library.Items.GetByStatusAsync(MediaItemStatus.Excluded, folder.Id, cancellationToken).ConfigureAwait(false))
            .Where(item => IndexedFolders.IsBackInRules(item, matcher));
        List<Id<MediaItem>> leftOver = [.. (await library.Items.GetByStatusAsync(MediaItemStatus.Discovered, folder.Id, cancellationToken).ConfigureAwait(false))
            .Concat(restored)
            .Select(item => item.Id)
            .Where(itemId => !queued.Contains(itemId))];
        await EnqueueAsync(context.Scheduler, jobId, leftOver, cancellationToken).ConfigureAwait(false);

        LogDiscovered(folder.DisplayPath, result.FilesFound, result.ItemsRegistered, leftOver.Count, result.Unsupported, result.UnreadableFiles);
    }

    private static async Task EnqueueAsync(IJobScheduler scheduler, Id<ProcessingJob> jobId, IReadOnlyList<Id<MediaItem>> itemIds, CancellationToken cancellationToken)
    {
        if (itemIds.Count > 0)
        {
            await scheduler.EnqueueWorkAsync(jobId, [.. itemIds.Select(itemId => new WorkItemRequest(ImportStages.Fingerprint, itemId))], cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Import of {Folder}: {FilesFound} media files, {Registered} new, {LeftOver} left from before queued again, {Unsupported} unsupported, {Unreadable} unreadable")]
    private partial void LogDiscovered(string folder, int filesFound, int registered, int leftOver, int unsupported, int unreadable);
}
