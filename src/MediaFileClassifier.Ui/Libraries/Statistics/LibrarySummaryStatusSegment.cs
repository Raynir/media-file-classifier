using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.StatusBar;

namespace MediaFileClassifier.Ui.Libraries.Statistics;

/// <summary>
/// Shows the library's item count at the right end of the status bar, e.g. "12,345 in library · 340 pending"
/// (E02-S05-T04). It updates while items are imported and analyzed; clicking it opens the library information window.
/// </summary>
/// <param name="monitor">Follows the library's counts.</param>
/// <param name="presenter">Opens the library information window.</param>
/// <param name="localizer">UI strings.</param>
public sealed class LibrarySummaryStatusSegment(
    LibraryStatisticsMonitor monitor,
    LibraryInfoPresenter presenter,
    Localizer localizer) : IStatusBarContributor
{
    /// <summary>
    /// The segment's ID.
    /// </summary>
    public const string SegmentId = "library.summary";

    private StatusBarSegment? segment;

    /// <inheritdoc />
    public void Register(StatusBarViewModel statusBar)
    {
        ArgumentNullException.ThrowIfNull(statusBar);

        // Order -1: the right edge, outside the processing segment (order 0).
        segment = statusBar.Add(new StatusBarSegment(SegmentId, StatusBarSide.Right, -1)
        {
            IconKey = "Icon.Library",
            IsVisible = false,
            Command = new RelayCommand(presenter.Show),
        });
        monitor.Changed += (_, _) => Show();
        localizer.Changed += (_, _) => Show();
        monitor.Start();
        Show();
    }

    private void Show()
    {
        if (segment is null)
        {
            return;
        }

        var statistics = monitor.Current;
        segment.IsVisible = statistics is not null;
        if (statistics is null)
        {
            return;
        }

        var total = Format.Number(statistics.Total);
        segment.Text = statistics.Pending > 0
            ? localizer.Format("StatusBar_LibrarySummaryPending", total, Format.Number(statistics.Pending))
            : localizer.Format("StatusBar_LibrarySummary", total);
        segment.ToolTip = localizer.Format(
            "StatusBar_LibrarySummary_ToolTip",
            Format.Number(statistics.Indexed),
            Format.Number(statistics.Pending),
            Format.Number(statistics.Errors),
            Format.Number(statistics.Missing));
    }
}
