using Avalonia.Controls;
using MediaFileClassifier.Ui.ViewModels.Activity;

namespace MediaFileClassifier.Ui.Views.Activity;

/// <summary>
/// The activity log (E15-S04): events newest first, grouped by day, with filter chips.
/// </summary>
public partial class ActivityLogView : UserControl
{
    private const double LoadMoreDistance = 240;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityLogView"/> class.
    /// </summary>
    public ActivityLogView()
    {
        InitializeComponent();
        EventList.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
    }

    // The next page loads when the list scrolls near its end, so it feels endless while only a page at a time is read.
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is ScrollViewer scroller
            && DataContext is ActivityLogViewModel viewModel
            && scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - LoadMoreDistance
            && viewModel.LoadMoreCommand.CanExecute(null))
        {
            viewModel.LoadMoreCommand.Execute(null);
        }
    }
}
