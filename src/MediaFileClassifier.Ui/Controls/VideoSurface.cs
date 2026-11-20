using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MediaFileClassifier.Ui.Details;

namespace MediaFileClassifier.Ui.Controls;

/// <summary>
/// Draws the video player's newest frame, fitted and centred (E14-S06, D070). It's an ordinary control, so the
/// player's controls and anything else can draw over it. While there's no frame it draws nothing, so the thumbnail
/// under it shows through.
/// </summary>
public sealed class VideoSurface : Control
{
    /// <summary>
    /// Defines the <see cref="Source"/> property.
    /// </summary>
    public static readonly StyledProperty<VideoFrameBuffer?> SourceProperty =
        AvaloniaProperty.Register<VideoSurface, VideoFrameBuffer?>(nameof(Source));

    private VideoFrameBuffer? subscribed;
    private WriteableBitmap? bitmap;

    static VideoSurface()
    {
        AffectsRender<VideoSurface>(SourceProperty);
    }

    /// <summary>
    /// Gets or sets the frames to draw.
    /// </summary>
    public VideoFrameBuffer? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether a frame is drawn, for tests.
    /// </summary>
    internal bool HasFrame => bitmap is not null;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (bitmap is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var source = new Rect(bitmap.Size);
        var scale = Math.Min(Bounds.Width / source.Width, Bounds.Height / source.Height);
        var size = source.Size * scale;
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        context.DrawImage(bitmap, source, new Rect((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2, size.Width, size.Height));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            Subscribe();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unsubscribe();
        bitmap?.Dispose();
        bitmap = null;
    }

    private void Subscribe()
    {
        Unsubscribe();
        if (Source is { } source && VisualRoot is not null)
        {
            subscribed = source;
            source.FrameReady += OnFrameReady;
            Present();
        }
    }

    private void Unsubscribe()
    {
        if (subscribed is not null)
        {
            subscribed.FrameReady -= OnFrameReady;
            subscribed = null;
        }
    }

    // On the decoder's thread.
    private void OnFrameReady(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Present, DispatcherPriority.Render);

    private void Present()
    {
        if (subscribed is null)
        {
            return;
        }

        bitmap = subscribed.Present(bitmap);
        InvalidateVisual();
    }
}
