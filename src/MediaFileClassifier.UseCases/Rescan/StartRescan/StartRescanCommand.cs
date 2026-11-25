using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Rescan.StartRescan;

/// <summary>
/// Starts a manual rescan (E02-S04-T06) of one folder, or of the whole library when <paramref name="FolderId"/> is
/// <c>null</c>. A library rescan is one job, so a file moved from one folder to another keeps its item (DNEW-5).
/// </summary>
/// <param name="FolderId">The folder, or <c>null</c> for every folder of the library.</param>
/// <param name="Quick">
/// Whether this is the quick sync on library open (E04-S03): a library-wide job over the Online folders at import
/// priority, whose summary is shown only when something changed. Folders in Manual mode are left out (E04-S05-T01). With a
/// <paramref name="FolderId"/>, it's the reconnect catch-up (E04-S04-T02) or a poll (E04-S05-T02) of that folder.
/// </param>
/// <param name="Library">
/// The library the caller means, or <c>null</c> for whichever is open. When another library is open by the time the
/// command runs, nothing is started and the outcome is <see cref="StartRescanOutcome.Unavailable"/> (MFC-1042), so a
/// sync meant for a library that was just closed doesn't land on the next one.
/// </param>
public sealed record StartRescanCommand(Id<IndexedFolder>? FolderId = null, bool Quick = false, IOpenLibrary? Library = null) : IRequest<StartRescanResult>;

/// <summary>
/// What <see cref="StartRescanCommand"/> did.
/// </summary>
/// <param name="JobId">The rescan job, or <c>null</c> when none was started.</param>
/// <param name="Outcome">Why a job was started or not.</param>
public sealed record StartRescanResult(Id<ProcessingJob>? JobId, StartRescanOutcome Outcome);

/// <summary>
/// Why <see cref="StartRescanCommand"/> started a job or not.
/// </summary>
public enum StartRescanOutcome
{
    /// <summary>The rescan job was queued.</summary>
    Started,

    /// <summary>There's no such folder, or no library or no folder to rescan.</summary>
    NothingToScan,

    /// <summary>The folder (or one of the library's folders) is being imported, rescanned or removed already.</summary>
    Busy,

    /// <summary>The platform has no background processing.</summary>
    Unavailable,
}

/// <summary>
/// Handles <see cref="StartRescanCommand"/>. A folder with unfinished import, rescan or removal work isn't rescanned,
/// as <c>ImportFoldersCommandHandler</c> does for imports; a library rescan waits while any such work exists. A folder
/// that can't be reached is marked Offline by the rescan itself (D188), so the summary can say so.
/// </summary>
/// <param name="logger">Logger.</param>
/// <param name="jobs">Runs the jobs of the open library, if the platform has background processing.</param>
public sealed class StartRescanCommandHandler(
    ILogger<StartRescanCommandHandler> logger,
    IJobSystem? jobs = null) : IRequestHandler<StartRescanCommand, StartRescanResult>
{
    /// <inheritdoc />
    public async Task<StartRescanResult> Handle(StartRescanCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (jobs?.Current is not { } scheduler
            || (request.Library is { } expected && !ReferenceEquals(scheduler.Library, expected)))
        {
            return new StartRescanResult(null, StartRescanOutcome.Unavailable);
        }

        var library = scheduler.Library;
        var folders = await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false);
        var targets = request.FolderId is { } folderId ? folders.Where(folder => folder.Id == folderId).ToList() : folders.ToList();
        if (request.Quick)
        {
            // A folder in Manual mode is synced only by a rescan the user starts (E04-S05-T01).
            targets = [.. targets.Where(folder => folder.Status == FolderStatus.Online && folder.MonitoringMode != MonitoringMode.Manual)];
        }

        if (targets.Count == 0)
        {
            return new StartRescanResult(null, StartRescanOutcome.NothingToScan);
        }

        var unfinished = (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.Type is ImportStages.ImportJob or RescanStages.RescanJob or RescanStages.QuickSyncJob or IndexedFolders.RemoveJob)

            // Waiting for a model means discovery and registration are done; the rest of the job needn't hold up a rescan.
            .Where(job => job.State != ProcessingJobState.WaitingForModel)
            .ToList();
        var busy = request.FolderId is { } single
            ? unfinished.Any(job => job.FolderId == single || (job.FolderId is null && job.Type is RescanStages.RescanJob or RescanStages.QuickSyncJob))
            : unfinished.Count > 0;
        if (busy)
        {
            logger.LogInformation("A rescan isn't started: import, rescan or removal work is unfinished");
            return new StartRescanResult(null, StartRescanOutcome.Busy);
        }

        var title = request.Quick ? "Quick sync" : request.FolderId is null ? "Rescan library" : $"Rescan {targets[0].DisplayPath}";
        var jobId = await scheduler.EnqueueJobAsync(
            new JobRequest(
                request.Quick ? RescanStages.QuickSyncJob : RescanStages.RescanJob,
                title,
                request.Quick ? JobPriority.Import : JobPriority.UserAction,
                [new WorkItemRequest(RescanStages.Reconcile)])
            {
                FolderId = request.FolderId,
            },
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Queued {Title}", title);
        return new StartRescanResult(jobId, StartRescanOutcome.Started);
    }
}
