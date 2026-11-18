using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Views.Browser;

/// <summary>
/// The virtualised thumbnail grid (E14-S01). Tells the view model what's on screen, scrolls when it asks, and turns
/// Ctrl+wheel and double-clicks into size changes and opening items. Clicks and navigation keys change the selection
/// (E14-S03); the scroll viewer keeps keyboard focus, because tile views are reused for other items as the grid
/// scrolls.
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
        Scroller.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        Scroller.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Scroller.LostFocus += (_, _) => viewModel?.IsFocusVisible = false;
        Scroller.AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
        {
            viewModel.ScrollRequested -= OnScrollRequested;
            viewModel.FocusRequested -= OnFocusRequested;
        }

        viewModel = DataContext as ThumbnailGridViewModel;
        if (viewModel is not null)
        {
            viewModel.ScrollRequested += OnScrollRequested;
            viewModel.FocusRequested += OnFocusRequested;
            ReportViewport();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ReportViewport();
    }

    private void OnFocusRequested(object? sender, EventArgs e) => Scroller.Focus(NavigationMethod.Tab);

    private static ThumbnailTileViewModel? TileOf(object? source) =>
        (source as StyledElement)?.DataContext as ThumbnailTileViewModel;

    private static GridMove? MoveFor(Key key) => key switch
    {
        Key.Left => GridMove.Left,
        Key.Right => GridMove.Right,
        Key.Up => GridMove.Up,
        Key.Down => GridMove.Down,
        Key.PageUp => GridMove.PageUp,
        Key.PageDown => GridMove.PageDown,
        Key.Home => GridMove.Home,
        Key.End => GridMove.End,
        _ => null,
    };

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

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Scroller);
        if (viewModel is null || (e.Source as Visual)?.FindAncestorOfType<ScrollBar>(includeSelf: true) is not null)
        {
            return;
        }

        Scroller.Focus(NavigationMethod.Pointer);
        if (point.Properties.IsRightButtonPressed)
        {
            // As in Windows Explorer: an unselected tile becomes the selection, a selected one keeps it, and the
            // space between tiles deselects, so the context menu then has nothing to show.
            if (TileOf(e.Source) is { Index: >= 0 } target)
            {
                _ = viewModel.ClickAsync(target.Index, target.IsSelected ? SelectionGesture.FocusOnly : SelectionGesture.Replace);
            }
            else
            {
                viewModel.ClearSelection();
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var control = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (TileOf(e.Source) is { Index: >= 0 } tile)
        {
            // The heart on the tile marks or unmarks just that item and leaves the selection alone (E11-S01).
            if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Border>().Any(border => border.Classes.Contains("TileFavorite")))
            {
                e.Handled = true;
                _ = viewModel.ToggleFavoriteAsync(tile);
                return;
            }

            var gesture = shift
                ? control ? SelectionGesture.AddRange : SelectionGesture.Range
                : control ? SelectionGesture.Toggle : SelectionGesture.Replace;

            // A double-click's second press keeps the selection the first one made.
            if (e.ClickCount < 2)
            {
                _ = viewModel.ClickAsync(tile.Index, gesture);
            }
        }
        else if (!control && !shift)
        {
            // A click on the space between tiles deselects everything, as in Windows Explorer.
            viewModel.IsFocusVisible = false;
            viewModel.ClearSelection();
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // Right-click or the Menu key: fill the menu before it opens, and keep it shut when nothing applies.
        var entries = viewModel?.BuildContextMenu() ?? [];
        ItemMenu.ItemsSource = entries;
        e.Handled = entries.Count == 0;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (viewModel is null || e.Handled)
        {
            return;
        }

        var control = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (MoveFor(e.Key) is { } move && (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            _ = viewModel.MoveAsync(move, shift, control);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && !shift && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            _ = viewModel.SelectFocusedAsync(toggle: control);
            e.Handled = true;
        }
    }
}
