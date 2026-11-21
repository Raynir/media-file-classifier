using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.UseCases.Analysis.Pipeline;
using MediaFileClassifier.UseCases.Common.Notifications;
using MediaFileClassifier.UseCases.Import;

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
public sealed class JobNotifications(IJobProgress progress, INotificationService notifications, Localizer localizer)
{
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
    /// The ID of the command that shows the items imported last (Recently imported), which the import notification's
    /// action runs.
    /// </summary>
    public const string ImportedItemsCommand = "library.show-recently-imported";

    private void OnJobFinished(object? sender, JobFinishedEventArgs e)
    {
        if (Describe(e) is { } notification)
        {
            notifications.Notify(notification);
        }
    }
}
