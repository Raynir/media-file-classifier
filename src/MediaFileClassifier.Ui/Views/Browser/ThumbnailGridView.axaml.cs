using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Views.Browser;

/// <summary>
/// The virtualised thumbnail grid (E14-S01). Tells the view model what's on screen, scrolls when it asks, and turns
/// Ctrl+wheel and double-clicks into size changes and opening items.
/// </summary>
public partial class ThumbnailGridView : UserControl
{
    private ThumbnailGridViewModel? viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailGridView"/> class.
    /// </summary>
    public ThumbnailGridView()
    {
        InitializeComponent();
        Scroller.ScrollChanged += (_, _) => ReportViewport();
        Scroller.SizeChanged += (_, _) => ReportViewport();
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        AddHandler(DoubleTappedEvent, OnDoubleTapped);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
        {
            viewModel.ScrollRequested -= OnScrollRequested;
            viewModel.FocusedTile = null;
        }

        viewModel = DataContext as ThumbnailGridViewModel;
        if (viewModel is not null)
        {
            viewModel.ScrollRequested += OnScrollRequested;
            viewModel.FocusedTile = FocusedTile;
            ReportViewport();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ReportViewport();
    }

    private static ThumbnailTileViewModel? TileOf(object? source) =>
        (source as StyledElement)?.DataContext as ThumbnailTileViewModel;

    private void ReportViewport()
    {
        if (viewModel is null || Scroller.Viewport.Width <= 0)
        {
            return;
        }

        var width = Scroller.Viewport.Width - RowsControl.Margin.Left - RowsControl.Margin.Right;
        viewModel.UpdateViewport(width, Scroller.Offset.Y, Scroller.Viewport.Height, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    }

    private void OnScrollRequested(object? sender, double offset) =>
        Dispatcher.UIThread.Post(() => Scroller.Offset = new Vector(Scroller.Offset.X, offset), DispatcherPriority.Loaded);

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (viewModel is null || (e.KeyModifiers & KeyModifiers.Control) == 0 || e.Delta.Y == 0)
        {
            return;
        }

        if (e.Delta.Y > 0)
        {
            viewModel.Larger();
        }
        else
        {
            viewModel.Smaller();
        }

        e.Handled = true;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (viewModel is not null && TileOf(e.Source) is { } tile)
        {
            _ = viewModel.OpenAsync(tile);
        }
    }

    private ThumbnailTileViewModel? FocusedTile() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused && this.IsVisualAncestorOf(focused)
            ? TileOf(focused)
            : null;
}
