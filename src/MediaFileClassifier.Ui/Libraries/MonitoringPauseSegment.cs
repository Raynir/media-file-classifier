using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.UseCases.Monitoring;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// The "Monitoring paused" notice in the status bar (E04-S06-T02): it shows only while folder monitoring is paused, and
/// clicking it resumes monitoring.
/// </summary>
/// <param name="pause">Pauses and resumes monitoring.</param>
/// <param name="localizer">UI strings.</param>
public sealed class MonitoringPauseSegment(MonitoringPause pause, Localizer localizer) : IStatusBarContributor
{
    /// <summary>
    /// The segment ID.
    /// </summary>
    public const string SegmentId = "libraries.monitoringPaused";

    private StatusBarSegment? segment;

    /// <inheritdoc />
    public void Register(StatusBarViewModel statusBar)
    {
        ArgumentNullException.ThrowIfNull(statusBar);

        segment = statusBar.Add(new StatusBarSegment(SegmentId, StatusBarSide.Right, 2)
        {
            IconKey = "Icon.Pause",
            Command = new RelayCommand(pause.Resume),
        });
        pause.Changed += (_, _) => Dispatcher.UIThread.Post(Show);
        localizer.Changed += (_, _) => Show();
        Show();
    }

    private void Show()
    {
        if (segment is null)
        {
            return;
        }

        segment.IsVisible = pause.IsPaused;
        segment.Text = localizer.Get("Monitoring_PausedStatus");
        segment.ToolTip = localizer.Get("Monitoring_ResumeTip");
    }
}
