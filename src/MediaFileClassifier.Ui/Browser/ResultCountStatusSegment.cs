using System.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The result count of the current view at the left end of the status bar (E13-S04-T02), e.g. "1,204 items", shown
/// while a library is open. It only shows information. The selected count (E14-S03) takes the next place, at
/// <see cref="SelectionOrder"/> with the ID <see cref="SelectionSegmentId"/>.
/// </summary>
/// <param name="grid">Gets the thumbnail grid, which knows the result count, once a library is open.</param>
/// <param name="session">Holds the open library.</param>
/// <param name="localizer">UI strings.</param>
public sealed class ResultCountStatusSegment(Func<ThumbnailGridViewModel> grid, ILibrarySession session, Localizer localizer) : IStatusBarContributor
{
    /// <summary>
    /// The ID of this segment.
    /// </summary>
    public const string SegmentId = "browser.counts";

    /// <summary>
    /// The ID that the selected count segment should use.
    /// </summary>
    public const string SelectionSegmentId = "browser.selection";

    /// <summary>
    /// The left-side order that the selected count segment should use, right after the result count.
    /// </summary>
    public const int SelectionOrder = 1;

    private StatusBarSegment? segment;
    private ThumbnailGridViewModel? watched;

    /// <inheritdoc />
    public void Register(StatusBarViewModel statusBar)
    {
        ArgumentNullException.ThrowIfNull(statusBar);

        segment = statusBar.Add(new StatusBarSegment(SegmentId, StatusBarSide.Left, 0));
        session.CurrentChanged += (_, _) => Show();
        localizer.Changed += (_, _) => Show();
        Show();
    }

    private void OnGridChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThumbnailGridViewModel.Count))
        {
            Show();
        }
    }

    private void Show()
    {
        if (segment is null)
        {
            return;
        }

        segment.IsVisible = session.Current is not null;
        if (segment.IsVisible && watched is null)
        {
            // The grid isn't built before the first library opens.
            watched = grid();
            watched.PropertyChanged += OnGridChanged;
        }

        var count = watched?.Count ?? 0;
        segment.Text = localizer.Format(count == 1 ? "StatusBar_Items_One" : "StatusBar_Items_Other", Format.Number(count));
        segment.ToolTip = localizer.Get("StatusBar_Items_ToolTip");
    }
}
