using Avalonia;
using Avalonia.Controls;
using MediaFileClassifier.Ui.ViewModels.Details;

namespace MediaFileClassifier.Ui.Views.Details;

/// <summary>
/// The video player in the preview (E14-S06). The code here pauses the video when the view leaves the window or the
/// window is minimized (the view model pauses while the preview or the panel is hidden), so it doesn't keep playing hidden.
/// </summary>
public partial class VideoPlayerView : UserControl
{
    private Window? window;

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoPlayerView"/> class.
    /// </summary>
    public VideoPlayerView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is not null)
        {
            window.PropertyChanged += OnWindowPropertyChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (window is not null)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
            window = null;
        }

        (DataContext as VideoPlayerViewModel)?.Pause();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && window?.WindowState == WindowState.Minimized)
        {
            (DataContext as VideoPlayerViewModel)?.Pause();
        }
    }
}
