using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Details;

namespace MediaFileClassifier.Ui.Views.Details;

/// <summary>
/// The preview and details panel (E14-S05). The code here wires the preview's zoom buttons and resize thumb, keyboard
/// focus from the grid and back (Esc), and tells the view model the zoom and the screen size.
/// </summary>
public partial class DetailsPanelView : UserControl
{
    private DetailsPanelViewModel? viewModel;
    private Id<MediaItem>? shownItem;

    /// <summary>
    /// Initializes a new instance of the <see cref="DetailsPanelView"/> class.
    /// </summary>
    public DetailsPanelView()
    {
        InitializeComponent();
        Focusable = true;
        FitButton.Click += (_, _) => Preview.Fit();
        ActualSizeButton.Click += (_, _) => Preview.ActualSize();
        PreviewResizer.DragDelta += OnResizerDragged;
        Preview.PropertyChanged += OnPreviewPropertyChanged;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        Preview.KeyDown += OnPreviewKeyDown;
        ShowZoom();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
        {
            viewModel.FocusRequested -= OnFocusRequested;
            viewModel.EditRequested -= OnEditRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        viewModel = DataContext as DetailsPanelViewModel;
        if (viewModel is not null)
        {
            viewModel.FocusRequested += OnFocusRequested;
            viewModel.EditRequested += OnEditRequested;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ReportScreen();
            if (viewModel.IsFocusPending)
            {
                // Enter in the grid came before the panel had an item to show.
                OnFocusRequested(this, EventArgs.Empty);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ReportScreen();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // The region drops the panel while nothing is selected; the next one may get a new view.
        if (viewModel is not null)
        {
            viewModel.FocusRequested -= OnFocusRequested;
            viewModel.EditRequested -= OnEditRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel = null;
        }
    }

    private void OnEditRequested(object? sender, EventArgs e) =>

        // The Overview tab may only now be selected, so focus the title once it's laid out.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (DataContext is DetailsPanelViewModel { IsBulk: true })
                {
                    BulkEditor.FocusFirstField();
                }
                else
                {
                    Editor.FocusFirstField();
                }
            },
            DispatcherPriority.Loaded);

    private void OnFocusRequested(object? sender, EventArgs e) =>

        // The panel may only now be shown or expanded, so focus once it's laid out.
        Dispatcher.UIThread.Post(
            () =>
            {
                var focused = Preview.IsEffectivelyVisible ? Preview.Focus(NavigationMethod.Tab) : Focus(NavigationMethod.Tab);
                if (focused)
                {
                    viewModel?.OnFocused();
                }
            },
            DispatcherPriority.Loaded);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Each item starts fitted, even when it's as large as the one before.
        if (e.PropertyName == nameof(DetailsPanelViewModel.Item) && viewModel?.Item?.Id != shownItem)
        {
            shownItem = viewModel?.Item?.Id;
            Preview.Fit();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && viewModel is not null)
        {
            viewModel.ReturnToBrowser();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // An animated image: Space plays or pauses, and , and . step a frame (E14-S12); the arrow keys still move through the results.
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || viewModel?.Animation is not { } animation)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                animation.TogglePlay();
                break;
            case Key.OemComma:
                animation.StepBack();
                break;
            case Key.OemPeriod:
                animation.StepForward();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnResizerDragged(object? sender, VectorEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PreviewHeight += e.Vector.Y;
        }
    }

    private void OnPreviewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ZoomImage.ZoomProperty)
        {
            ShowZoom();
            _ = viewModel?.OnZoomChangedAsync(Preview.Zoom);
        }
    }

    private void ShowZoom() => ZoomText.Text = Localizer.Current.Format("Details_Preview_Zoom", Preview.Zoom * 100);

    private void ReportScreen()
    {
        if (viewModel is not null && TopLevel.GetTopLevel(this)?.Screens?.ScreenFromVisual(this) is { } screen)
        {
            viewModel.ScreenPixels = Math.Max(screen.Bounds.Width, screen.Bounds.Height);
        }
    }
}
