using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.StatusBar;

namespace MediaFileClassifier.Ui.Processing;

/// <summary>
/// The pause button in the status bar (E05-S05-T02), next to the processing indicator: it pauses all processing, or
/// resumes everything that's paused. It shows while work is active or anything is paused.
/// </summary>
/// <param name="feed">The throttled job progress.</param>
/// <param name="pause">Pauses and resumes processing.</param>
/// <param name="localizer">UI strings.</param>
public sealed class ProcessingPauseSegment(ProgressFeed feed, ProcessingPause pause, Localizer localizer) : IStatusBarContributor
{
    /// <summary>
    /// The segment ID.
    /// </summary>
    public const string SegmentId = "processing.pause";

    private StatusBarSegment? segment;

    /// <inheritdoc />
    public void Register(StatusBarViewModel statusBar)
    {
        ArgumentNullException.ThrowIfNull(statusBar);

        // Order -1, like the library summary, and registered after it: it sits between the processing indicator (0)
        // and the summary.
        segment = statusBar.Add(new StatusBarSegment(SegmentId, StatusBarSide.Right, -1) { Command = new RelayCommand(pause.Toggle) });
        feed.Updated += (_, _) => Show();
        pause.Changed += (_, _) => Show();
        localizer.Changed += (_, _) => Show();
        Show();
    }

    private void Show()
    {
        if (segment is null)
        {
            return;
        }

        var paused = pause.IsAnythingPaused;
        segment.IsVisible = paused || feed.Current.ActiveJobCount > 0;
        segment.IconKey = paused ? "Icon.Play" : "Icon.Pause";
        segment.ToolTip = localizer.Get(paused ? "Processing_ResumeAll" : "Processing_PauseAll");
    }
}
