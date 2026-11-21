using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.StatusBar;

namespace MediaFileClassifier.Ui.Maintenance;

/// <summary>
/// Shows the running library maintenance task in the status bar, e.g. "Refreshing the search index… 42%"
/// (E02-S06-T01). Clicking it asks whether to stop the task.
/// </summary>
/// <param name="runner">Runs the tasks.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="dialogs">Asks before stopping, or <c>null</c> to stop without asking.</param>
/// <param name="post">Runs an update on the UI thread; tests run it at once. <c>null</c> posts to the dispatcher.</param>
public sealed class MaintenanceStatusSegment(
    MaintenanceRunner runner,
    Localizer localizer,
    MaintenanceDialogs? dialogs = null,
    Action<Action>? post = null) : IStatusBarContributor
{
    /// <summary>
    /// The segment's ID.
    /// </summary>
    public const string SegmentId = "library.maintenance";

    private StatusBarSegment? segment;

    /// <summary>
    /// Describes a running task.
    /// </summary>
    /// <param name="localizer">UI strings.</param>
    /// <param name="task">The task.</param>
    /// <param name="progress">The share done, or <c>null</c>.</param>
    /// <returns>The text.</returns>
    public static string Describe(Localizer localizer, MaintenanceTask task, double? progress)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        var text = localizer.Get(task == MaintenanceTask.RefreshIndex ? "Maintenance_RefreshingIndex" : "Maintenance_CheckingIntegrity");
        return progress is { } share ? localizer.Format("Maintenance_Progress", text, Math.Floor(share * 100)) : text;
    }

    /// <inheritdoc />
    public void Register(StatusBarViewModel statusBar)
    {
        ArgumentNullException.ThrowIfNull(statusBar);

        segment = statusBar.Add(new StatusBarSegment(SegmentId, StatusBarSide.Right, 2)
        {
            IconKey = "Icon.Status.Pending",
            IsVisible = false,
            Command = new AsyncRelayCommand(StopAsync),
        });
        runner.Changed += (_, _) => (post ?? (action => Dispatcher.UIThread.Post(action)))(Show);
        localizer.Changed += (_, _) => Show();
        Show();
    }

    private void Show()
    {
        if (segment is null)
        {
            return;
        }

        var task = runner.Current;
        segment.IsVisible = task is not null;
        if (task is { } current)
        {
            segment.Text = Describe(localizer, current, runner.Progress);
            segment.ToolTip = localizer.Get("Maintenance_StopToolTip");
        }
    }

    private async Task StopAsync()
    {
        if (runner.Current is not { } task)
        {
            return;
        }

        var question = localizer.Get(task == MaintenanceTask.RefreshIndex ? "Maintenance_StopRefresh" : "Maintenance_StopCheck");
        if (dialogs is null || await dialogs.AskAsync(question, localizer.Get("Maintenance_Stop"), localizer.Get("Maintenance_KeepGoing")))
        {
            runner.Cancel();
        }
    }
}
