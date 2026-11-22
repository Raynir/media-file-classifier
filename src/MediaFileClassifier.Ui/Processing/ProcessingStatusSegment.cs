using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.StatusBar;

namespace MediaFileClassifier.Ui.Processing;

/// <summary>
/// The processing indicator in the status bar (E05-S02-T03): what the jobs are doing and how long they'll take, shown
/// whenever a job is active. Clicking it opens the processing panel (E05-S03).
/// </summary>
/// <param name="feed">The throttled job progress.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="panel">Opens the processing panel, or <see langword="null"/> where there's no window for it.</param>
/// <param name="jobs">The job system, to show when processing waits for Resume, or <see langword="null"/> for none.</param>
/// <param name="pause">Tells when processing is paused or resumed (E05-S05), or <see langword="null"/> to notice only with the next progress update.</param>
public sealed class ProcessingStatusSegment(
    ProgressFeed feed,
    Localizer localizer,
    ProcessingPanelPresenter? panel = null,
    IJobSystem? jobs = null,
    ProcessingPause? pause = null) : IStatusBarContributor
{
    /// <summary>
    /// The segment ID.
    /// </summary>
    public const string SegmentId = "processing.progress";

    private readonly ProgressText text = new(localizer);
    private StatusBarSegment? segment;

    /// <inheritdoc />
    public void Register(StatusBarViewModel statusBar)
    {
        ArgumentNullException.ThrowIfNull(statusBar);

        segment = statusBar.Add(new StatusBarSegment(SegmentId, StatusBarSide.Right, 0) { IconKey = "Icon.Status.Pending", Command = panel is null ? null : new RelayCommand(panel.Show) });
        feed.Updated += (_, summary) => Show(summary);
        if (pause is not null)
        {
            pause.Changed += (_, _) => Show(feed.Current);
        }

        localizer.Changed += (_, _) => Show(feed.Current);
        Show(feed.Current);
    }

    private void Show(ProgressSummary summary)
    {
        if (segment is null)
        {
            return;
        }

        // Processing that waits for Resume counts its unfinished jobs, but nothing runs: say why it's idle.
        if (jobs?.Current?.IsPaused == true)
        {
            segment.IsVisible = true;
            segment.Text = localizer.Get("Processing_PausedStatus");
            segment.ToolTip = localizer.Get("Processing_Paused");
            return;
        }

        segment.IsVisible = summary.ActiveJobCount > 0;
        segment.Text = text.Summary(summary);
        segment.ToolTip = text.Details(summary);
    }
}
