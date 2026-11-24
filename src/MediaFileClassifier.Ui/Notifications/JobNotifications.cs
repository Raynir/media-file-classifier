using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Navigation;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.UseCases.Analysis.Pipeline;
using MediaFileClassifier.UseCases.Common.Notifications;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Rescan;
using MediaFileClassifier.UseCases.Rescan.GetRescanSummary;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Ui.Notifications;

/// <summary>
/// Raises one summary notification when an import or analysis job ends (E15-S01-T02), however many items it had: the
/// job system reports each job once, when it has run to its end, so 1,000 items make one notification, not 1,000.
/// A job that fully succeeded is a success, one with failed items a warning with "View failed items", and one whose
/// every item failed an error. Cancelled jobs end silently. Other job types, such as hashing, say nothing.
/// </summary>
/// <param name="progress">Reports finished jobs, on a background thread.</param>
/// <param name="notifications">Shows the notifications; safe to call from any thread.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="summaries">Reads a quick sync's summary, or <see langword="null"/> to leave quick syncs silent.</param>
/// <param name="logger">Logs a quick sync summary that couldn't be made, or <see langword="null"/> for none.</param>
/// <param name="tally">Tells which import jobs are folder monitoring's (E04-S06-T03), or <see langword="null"/> for none.</param>
/// <param name="settings">Holds the size a monitored batch must have to be notified, or <see langword="null"/> for the default.</param>
public sealed class JobNotifications(
    IJobProgress progress,
    INotificationService notifications,
    Localizer localizer,
    ISender? summaries = null,
    ILogger<JobNotifications>? logger = null,
    MonitoringTally? tally = null,
    ISettingsStore? settings = null)
{
    private const int QuickSyncSummaryAttempts = 5;
    private static readonly TimeSpan QuickSyncSummaryWait = TimeSpan.FromMilliseconds(200);
    private int started;

    /// <summary>
    /// Starts listening. Calling it again does nothing.
    /// </summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 0)
        {
            progress.JobFinished += OnJobFinished;
        }
    }

    /// <summary>
    /// Makes the notification for a finished job.
    /// </summary>
    /// <param name="finished">The finished job.</param>
    /// <returns>The notification, or <see langword="null"/> when the job type has none or the job had no items.</returns>
    public Notification? Describe(JobFinishedEventArgs finished)
    {
        ArgumentNullException.ThrowIfNull(finished);

        var job = finished.Progress;
        var isImport = job.Type == ImportStages.ImportJob;
        if (isImport && tally?.Get(job.JobId) is { } monitored)
        {
            return DescribeMonitoring(finished, monitored);
        }

        if (!isImport && job.Type != AnalysisStages.AnalysisJob)
        {
            return null;
        }

        // Every item goes through each per-item stage, so the busiest one counts the items.
        var items = job.Stages.Where(stage => !stage.IsWholeTarget).Select(stage => stage.Total).DefaultIfEmpty(0).Max();
        if (items == 0)
        {
            return null;
        }

        var failed = Math.Min(items, job.Failed);
        var done = items - failed;
        var kind = isImport ? "Import" : "Analysis";
        if (failed == 0)
        {
            var action = isImport
                ? new NotificationAction(localizer.Get("Notification_ShowImported"), ImportedItemsCommand)
                : null;
            return new Notification(
                localizer.Format(done == 1 ? $"Notification_{kind}Done_One" : $"Notification_{kind}Done_Other", Format.Number(done)),
                NotificationSeverity.Success,
                action);
        }

        var viewFailed = new NotificationAction(localizer.Get("Notification_ViewFailed"), ProcessingCommands.OpenQueue);
        return done == 0
            ? new Notification(localizer.Format($"Notification_{kind}Failed", Format.Number(failed)), NotificationSeverity.Error, viewFailed)
            : new Notification(localizer.Format($"Notification_{kind}Partial", Format.Number(done), Format.Number(failed)), NotificationSeverity.Warning, viewFailed);
    }

    /// <summary>
    /// Makes the notification for a finished monitoring job (E04-S06-T03): what the changes of a watched folder did, or
    /// nothing when they number fewer than the setting's threshold (they're in the activity log either way).
    /// </summary>
    private Notification? DescribeMonitoring(JobFinishedEventArgs finished, MonitoringCounts changes)
    {
        var threshold = settings?.Get(MonitoringSettings.NotifyThreshold) ?? (int)MonitoringSettings.NotifyThreshold.DefaultValue;
        var failed = finished.Progress.Failed;
        if (finished.State != ProcessingJobState.Completed || changes.Total < threshold)
        {
            return null;
        }

        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(changes.Folder)) is { Length: > 0 } name ? name : changes.Folder;
        var text = changes is { Modified: 0, Moved: 0, Missing: 0 }
            ? localizer.Format(changes.Added == 1 ? "Notification_MonitoringNew_One" : "Notification_MonitoringNew_Other", Format.Number(changes.Added), folder)
            : localizer.Format("Notification_MonitoringMixed", Format.Number(changes.Total), folder, MonitoringDetails(changes));
        if (failed == 0)
        {
            return new Notification(text, NotificationSeverity.Info, new NotificationAction(localizer.Get("Notification_ShowImported"), ImportedItemsCommand));
        }

        return new Notification(
            text + " " + localizer.Format("Notification_MonitoringFailed", Format.Number(failed)),
            NotificationSeverity.Warning,
            new NotificationAction(localizer.Get("Notification_ViewFailed"), ProcessingCommands.OpenQueue));
    }

    private string MonitoringDetails(MonitoringCounts changes) => string.Join(
        ", ",
        new[]
        {
            (changes.Added, "Notification_MonitoringPart_New"),
            (changes.Modified, "Notification_MonitoringPart_Updated"),
            (changes.Moved, "Notification_MonitoringPart_Moved"),
            (changes.Missing, "Notification_MonitoringPart_Missing"),
        }.Where(part => part.Item1 > 0).Select(part => localizer.Format(part.Item2, Format.Number(part.Item1))));

    /// <summary>
    /// The ID of the command that shows the items imported last (Recently imported), which the import notification's
    /// action runs.
    /// </summary>
    public const string ImportedItemsCommand = NavigationCommands.ShowRecentlyImported;

    /// <summary>
    /// Makes the notification for a finished quick sync (E04-S03): its summary, or nothing when it found no change.
    /// </summary>
    /// <param name="finished">The finished job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The notification, or <see langword="null"/> when nothing changed, the job isn't a quick sync, or it can't be told.</returns>
    public async Task<Notification?> DescribeQuickSyncAsync(JobFinishedEventArgs finished, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finished);

        if (finished.Progress.Type != RescanStages.QuickSyncJob || finished.State != ProcessingJobState.Completed || summaries is null)
        {
            return null;
        }

        // The job's end may be stored a moment after it's reported.
        RescanSummary? summary = null;
        for (var attempt = 0; attempt < QuickSyncSummaryAttempts && summary is null; attempt++)
        {
            summary = await summaries.Send(new GetRescanSummaryQuery(finished.Progress.JobId), cancellationToken).ConfigureAwait(false);
            if (summary is null)
            {
                await Task.Delay(QuickSyncSummaryWait, cancellationToken).ConfigureAwait(false);
            }
        }

        if (summary?.Counts is not { } counts || counts.New + counts.Moved + counts.Missing + counts.Modified == 0)
        {
            return null;
        }

        return new Notification(
            localizer.Format("Notification_QuickSyncDone", Format.Number(counts.New), Format.Number(counts.Moved), Format.Number(counts.Missing), Format.Number(counts.Modified)),
            NotificationSeverity.Info);
    }

    private async Task NotifyQuickSyncAsync(JobFinishedEventArgs finished)
    {
        try
        {
            if (await DescribeQuickSyncAsync(finished).ConfigureAwait(false) is { } notification)
            {
                notifications.Notify(notification);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // E.g. the library closed meanwhile: there's no summary to show. Nothing waits for this task, so it mustn't throw.
            logger?.LogWarning(exception, "Couldn't summarise the quick sync");
        }
    }

    private void OnJobFinished(object? sender, JobFinishedEventArgs e)
    {
        if (e.Progress.Type == RescanStages.QuickSyncJob)
        {
            _ = NotifyQuickSyncAsync(e);
            return;
        }

        if (Describe(e) is { } notification)
        {
            notifications.Notify(notification);
        }
    }
}
