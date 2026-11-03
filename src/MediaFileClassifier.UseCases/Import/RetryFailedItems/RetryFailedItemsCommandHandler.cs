using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Import.Stages;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Import.RetryFailedItems;

/// <summary>
/// Handles <see cref="RetryFailedItemsCommand"/>. Import stages are queued in the given import job, or in a new import
/// job per folder; hashing goes to the folder's hash job.
/// </summary>
/// <param name="jobs">Runs the jobs of the open library.</param>
/// <param name="files">Checks whether a file has changed.</param>
/// <param name="hashJobs">Queues full hashing.</param>
/// <param name="logger">Logger.</param>
public sealed class RetryFailedItemsCommandHandler(
    IJobSystem jobs,
    IMediaFileSource files,
    FullHashJobs hashJobs,
    ILogger<RetryFailedItemsCommandHandler> logger) : IRequestHandler<RetryFailedItemsCommand, RetryFailedItemsResult>
{
    /// <inheritdoc />
    public async Task<RetryFailedItemsResult> Handle(RetryFailedItemsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scheduler = jobs.Current ?? throw new InvalidOperationException("No library is open.");
        var library = scheduler.Library;
        ProcessingJob? job = null;
        if (request.JobId is { } jobId)
        {
            job = await library.Jobs.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false) ?? throw new ArgumentException("There's no such job.", nameof(request));
        }

        IEnumerable<MediaItem> failed = await library.Items.GetByStatusAsync(MediaItemStatus.Error, job?.FolderId, cancellationToken).ConfigureAwait(false);

        // A job without a folder, e.g. for a set of items: only the items it worked on.
        if (job is { FolderId: null })
        {
            var itemIds = (await library.Jobs.GetWorkItemsAsync(job.Id, cancellationToken).ConfigureAwait(false))
                .Where(workItem => workItem.ItemId is not null)
                .Select(workItem => workItem.ItemId!.Value)
                .ToHashSet();
            failed = failed.Where(item => itemIds.Contains(item.Id));
        }

        var retried = 0;
        var unchanged = 0;
        foreach (var byFolder in failed.Where(item => item.Error is not null).GroupBy(item => item.FolderId))
        {
            var folder = await library.Folders.GetAsync(byFolder.Key, cancellationToken).ConfigureAwait(false);
            if (folder is null)
            {
                continue;
            }

            var importWork = new List<WorkItemRequest>();
            var hashWork = new List<Id<MediaItem>>();
            foreach (var item in byFolder)
            {
                var stage = item.Error!.Stage;
                if (item.Error.IsPermanent)
                {
                    if (!await RestartIfChangedAsync(library.Items, folder, item, cancellationToken).ConfigureAwait(false))
                    {
                        unchanged++;
                        continue;
                    }

                    stage = ImportStages.Fingerprint;
                }

                if (stage == ImportStages.ContentHash)
                {
                    hashWork.Add(item.Id);
                }
                else
                {
                    importWork.Add(new WorkItemRequest(stage, item.Id));
                }

                retried++;
            }

            await EnqueueImportAsync(scheduler, job, folder, importWork, cancellationToken).ConfigureAwait(false);
            await hashJobs.EnqueueAsync(scheduler, folder, hashWork, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Retrying {Retried} failed items; {Unchanged} damaged files haven't changed", retried, unchanged);
        return new RetryFailedItemsResult(retried, unchanged);
    }

    // Times are stored as Unix milliseconds (D071), so the file's time is cut to match before comparing.
    private static DateTimeOffset ToStoredPrecision(DateTimeOffset time) =>
        DateTimeOffset.FromUnixTimeMilliseconds(time.ToUnixTimeMilliseconds());

    private static async Task EnqueueImportAsync(
        IJobScheduler scheduler,
        ProcessingJob? job,
        IndexedFolder folder,
        IReadOnlyList<WorkItemRequest> work,
        CancellationToken cancellationToken)
    {
        if (work.Count == 0)
        {
            return;
        }

        if (job?.Type == ImportStages.ImportJob && await scheduler.EnqueueWorkAsync(job.Id, work, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await scheduler.EnqueueJobAsync(
            new JobRequest(ImportStages.ImportJob, $"Retry failed items in {folder.DisplayPath}", JobPriority.Import, work) { FolderId = folder.Id },
            cancellationToken).ConfigureAwait(false);
    }

    // A damaged file is worth another try only once it has changed; then everything known about it is stale.
    private async Task<bool> RestartIfChangedAsync(
        IMediaItemRepository items,
        IndexedFolder folder,
        MediaItem item,
        CancellationToken cancellationToken)
    {
        var stat = await files.GetStatAsync(ItemStage.GetLocation(folder, item), cancellationToken).ConfigureAwait(false);
        if (stat is null || (stat.Length == item.SizeBytes && ToStoredPrecision(stat.LastModifiedUtc) == item.FileModifiedAt))
        {
            return false;
        }

        item.UpdateFile(stat.Length, stat.CreatedUtc ?? stat.LastModifiedUtc, stat.LastModifiedUtc, new FileHashes());
        await items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
