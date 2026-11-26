using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Activity.RecordActivity;
using MediaFileClassifier.UseCases.Analysis.Pipeline;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Maintenance;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Rescan;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Activity;

/// <summary>
/// Records an activity event when an import, rescan, analysis or maintenance job ends (E15-S04-T01): the job system
/// reports each job once, when it has run to its end, so a job of 1,000 items makes one event. A job that fully
/// succeeded is Info, one with failed items a Warning, and one whose every item failed an Error. Cancelled jobs end
/// without an event. Other job types, such as hashing, are not significant enough to list.
/// </summary>
/// <param name="progress">Reports finished jobs, on a background thread.</param>
/// <param name="session">The open library.</param>
/// <param name="sender">Records the events.</param>
/// <param name="logger">Logs a job that could not be recorded.</param>
/// <param name="tally">Tells which import jobs are monitoring jobs, or <see langword="null"/> for none.</param>
public sealed partial class ActivityJobRecorder(
    IJobProgress progress,
    ILibrarySession session,
    ISender sender,
    ILogger<ActivityJobRecorder> logger,
    MonitoringTally? tally = null)
{
    private int started;

    /// <summary>
    /// Starts listening. Calling it again does nothing.
    /// </summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 0)
        {
            progress.JobFinished += (_, finished) => _ = RecordSafelyAsync(finished);
        }
    }

    /// <summary>
    /// Records the event of a finished job, if its type has one.
    /// </summary>
    /// <param name="finished">The finished job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the event is recorded.</returns>
    public async Task RecordAsync(JobFinishedEventArgs finished, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finished);

        var job = finished.Progress;
        var type = job.Type switch
        {
            ImportStages.ImportJob => ActivityEventType.Import,
            RescanStages.RescanJob or RescanStages.QuickSyncJob => ActivityEventType.Rescan,
            AnalysisStages.AnalysisJob => ActivityEventType.Analysis,
            MaintenanceJobs.ThumbnailsJob => ActivityEventType.Maintenance,
            _ => (ActivityEventType?)null,
        };
        if (type is null || session.Current is not { } library)
        {
            return;
        }

        // The stored job knows the folder it ran for and, for a rescan, its summary.
        var stored = await library.Jobs.GetJobAsync(job.JobId, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return;
        }

        var folder = stored.FolderId is { } folderId
            ? await library.Folders.GetAsync(folderId, cancellationToken).ConfigureAwait(false)
            : null;
        var subject = type == ActivityEventType.Maintenance ? null : folder?.DisplayPath ?? stored.Description;

        // Every item goes through each per-item stage, so the busiest one counts the items.
        var items = job.Stages.Where(stage => !stage.IsWholeTarget).Select(stage => stage.Total).DefaultIfEmpty(0).Max();
        var failed = Math.Min(items, job.Failed);
        var counts = new Dictionary<string, long>();
        if (type == ActivityEventType.Import && tally?.Get(job.JobId) is { } changed)
        {
            // Changes found by folder monitoring (E04-S06-T03): one event for the burst, with what it did.
            type = ActivityEventType.FolderMonitoring;
            if (changed.Total == 0 && failed == 0)
            {
                return;
            }

            counts[ActivityCounts.Added] = changed.Added;
            counts[ActivityCounts.Modified] = changed.Modified;
            counts[ActivityCounts.Moved] = changed.Moved;
            counts[ActivityCounts.Missing] = changed.Missing;
            counts[ActivityCounts.Failed] = failed;
        }
        else if (type == ActivityEventType.Rescan)
        {
            if (RescanCounts.FromJson(stored.Result) is { } rescan)
            {
                // A quick sync on open says nothing when it found nothing to do (E04-S03).
                if (job.Type == RescanStages.QuickSyncJob && finished.State == ProcessingJobState.Completed && failed == 0
                    && rescan.New + rescan.Moved + rescan.Missing + rescan.Modified == 0)
                {
                    return;
                }

                counts[ActivityCounts.Folders] = rescan.Folders;
                counts[ActivityCounts.Offline] = rescan.Offline;
                counts[ActivityCounts.Added] = rescan.New;
                counts[ActivityCounts.Moved] = rescan.Moved;
                counts[ActivityCounts.Missing] = rescan.Missing;
                counts[ActivityCounts.Modified] = rescan.Modified;
            }

            counts[ActivityCounts.Failed] = failed;
        }
        else
        {
            if (items == 0 && finished.State != ProcessingJobState.Failed)
            {
                return;
            }

            counts[ActivityCounts.Items] = items;
            counts[ActivityCounts.Failed] = failed;
        }

        var severity = ActivitySeverity.Info;
        if (finished.State == ProcessingJobState.Failed || (items > 0 && failed >= items))
        {
            severity = ActivitySeverity.Error;
        }
        else if (failed > 0)
        {
            severity = ActivitySeverity.Warning;
        }

        await sender.Send(new RecordActivityCommand(type.Value, severity, subject, counts, folder?.Id), cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordSafelyAsync(JobFinishedEventArgs finished)
    {
        try
        {
            await RecordAsync(finished).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex, finished.Progress.Type);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record the end of a {JobType} job in the activity log")]
    private static partial void LogFailed(ILogger logger, Exception exception, string jobType);
}
