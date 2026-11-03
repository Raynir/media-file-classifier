using System.ComponentModel;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.StatusBar;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The selected count in the status bar (E14-S03, E13-S04-T02), e.g. "3 selected", right after the result count.
/// It shows only while something is selected.
/// </summary>
/// <param name="selection">The media browser's selection.</param>
/// <param name="localizer">UI strings.</param>
public sealed class SelectionStatusSegment(SelectionModel selection, Localizer localizer) : IStatusBarContributor
{
    /// <summary>
    /// The segment ID.
    /// </summary>
    public const string SegmentId = "browser.selection";

    private StatusBarSegment? segment;

    /// <inheritdoc />
    public void Register(StatusBarViewModel statusBar)
    {
        ArgumentNullException.ThrowIfNull(statusBar);

        // Order 1: right after the result count (order 0).
        segment = statusBar.Add(new StatusBarSegment(SegmentId, StatusBarSide.Left, 1));
        selection.PropertyChanged += OnSelectionChanged;
        localizer.Changed += (_, _) => Show();
        Show();
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionModel.Count))
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

        var count = selection.Count;
        segment.IsVisible = count > 0;
        segment.Text = localizer.Format(count == 1 ? "StatusBar_Selected_One" : "StatusBar_Selected_Other", Format.Number(count));
        segment.ToolTip = localizer.Get("StatusBar_Selected_ToolTip");
    }
}
