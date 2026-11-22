using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Rescan;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Maintenance.RegenerateThumbnails;

/// <summary>
/// Queues a thumbnail job for the open library's items (E02-S06-T01, T02): by default those whose thumbnails are
/// missing or older than their file; with <see cref="All"/>, every item, after the whole thumbnail cache is deleted
/// (keyframe pictures stay). Items on screen go first through the grid's boost, so the job runs at import priority:
/// ahead of hashing and analysis, behind user actions.
/// </summary>
/// <param name="All">Whether to delete and remake every thumbnail.</param>
public sealed record RegenerateThumbnailsCommand(bool All = false) : IRequest<RegenerateThumbnailsResult>;

/// <summary>
/// What <see cref="RegenerateThumbnailsCommand"/> did.
/// </summary>
/// <param name="Outcome">Whether the job started, and why not.</param>
/// <param name="ItemCount">The number of items queued.</param>
/// <param name="JobId">The job, when it started.</param>
public sealed record RegenerateThumbnailsResult(RegenerateThumbnailsOutcome Outcome, int ItemCount = 0, Id<ProcessingJob>? JobId = null);

/// <summary>
/// Whether a thumbnail job started.
/// </summary>
public enum RegenerateThumbnailsOutcome
{
    /// <summary>
    /// The job is queued.
    /// </summary>
    Started,

    /// <summary>
    /// Every item has its thumbnails; nothing was queued.
    /// </summary>
    NothingToDo,

    /// <summary>
    /// A thumbnail job is still unfinished.
    /// </summary>
    Busy,

    /// <summary>
    /// An import or rescan is still unfinished. It writes thumbnails itself, and "All" would delete them under it
    /// (user's choice, 2026-11-21).
    /// </summary>
    ImportRunning,

    /// <summary>
    /// No library is open, or processing isn't available.
    /// </summary>
    Unavailable,
}

/// <summary>
/// Handles <see cref="RegenerateThumbnailsCommand"/>.
/// </summary>
/// <param name="thumbnails">Finds the thumbnails.</param>
/// <param name="logger">Logger.</param>
/// <param name="jobs">The job system, or <c>null</c> where there's none.</param>
public sealed class RegenerateThumbnailsCommandHandler(
    IThumbnailCache thumbnails,
    ILogger<RegenerateThumbnailsCommandHandler> logger,
    IJobSystem? jobs = null) : IRequestHandler<RegenerateThumbnailsCommand, RegenerateThumbnailsResult>
{
    /// <summary>
    /// How many items are read at a time.
    /// </summary>
    internal const int PageSize = 2000;

    /// <summary>
    /// Makes the busy check and the enqueue one step, so two quick clicks don't queue two jobs.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <inheritdoc />
    public async Task<RegenerateThumbnailsResult> Handle(RegenerateThumbnailsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (jobs?.Current is not { } scheduler)
        {
            return new(RegenerateThumbnailsOutcome.Unavailable);
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await QueueAsync(scheduler, request.All, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<RegenerateThumbnailsResult> QueueAsync(IJobScheduler scheduler, bool all, CancellationToken cancellationToken)
    {
        var library = scheduler.Library;
        var unfinished = await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false);
        if (unfinished.Any(job => job.Type == MaintenanceJobs.ThumbnailsJob))
        {
            return new(RegenerateThumbnailsOutcome.Busy);
        }

        if (unfinished.Any(job => job.Type is ImportStages.ImportJob or RescanStages.RescanJob or RescanStages.QuickSyncJob))
        {
            return new(RegenerateThumbnailsOutcome.ImportRunning);
        }

        // One file check or deletion per item, so off the caller's thread.
        var items = await Task.Run(
            async () =>
            {
                if (all)
                {
                    thumbnails.Clear(library.Layout, cancellationToken);
                }

                return await FindItemsAsync(library, all, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        if (items.Count == 0)
        {
            logger.LogInformation("Every item of {Library} has its thumbnails", library.Library.Name);
            return new(RegenerateThumbnailsOutcome.NothingToDo);
        }

        var jobId = await scheduler.EnqueueJobAsync(
            new JobRequest(
                MaintenanceJobs.ThumbnailsJob,
                "Regenerate thumbnails",
                JobPriority.Import,
                [.. items.Select(id => new WorkItemRequest(MaintenanceJobs.ThumbnailsStage, id, all ? MaintenanceJobs.ForcePayload : null))]),
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Queued thumbnails for {Count} items of {Library} (all: {All})", items.Count, library.Library.Name, all);
        return new(RegenerateThumbnailsOutcome.Started, items.Count, jobId);
    }

    private async Task<List<Id<MediaItem>>> FindItemsAsync(IOpenLibrary library, bool all, CancellationToken cancellationToken)
    {
        var items = new List<Id<MediaItem>>();
        for (var offset = 0; ; offset += PageSize)
        {
            var page = await library.Items.GetSummariesAsync(offset, PageSize, cancellationToken).ConfigureAwait(false);
            items.AddRange(page
                .Where(item => MaintenanceJobs.HasThumbnails(item.Status)
                    && (all || RegenerateThumbnailStage.NeedsThumbnails(thumbnails, library.Layout, item.Id, item.FileModifiedAt)))
                .Select(item => item.Id));
            if (page.Count < PageSize)
            {
                return items;
            }
        }
    }
}
