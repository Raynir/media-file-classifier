using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MediaFileClassifier.Ui.ViewModels.Duplicates;

namespace MediaFileClassifier.Ui.Controls;

/// <summary>
/// A preview in zoom compare (E12-S03-T04). It draws the part of its image that a shared <see cref="ZoomState"/>
/// names, so every preview bound to the same state shows the same area: the mouse wheel zooms, dragging pans, and a
/// double-click shows the whole image again.
/// </summary>
public sealed class ZoomImage : Control
{
    private const double WheelStep = 1.2;

    /// <summary>
    /// Defines the <see cref="Source"/> property.
    /// </summary>
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<ZoomImage, IImage?>(nameof(Source));

    /// <summary>
    /// Defines the <see cref="Zoom"/> property.
    /// </summary>
    public static readonly StyledProperty<ZoomState?> ZoomProperty = AvaloniaProperty.Register<ZoomImage, ZoomState?>(nameof(Zoom));

    private Point? dragFrom;
    private bool isAttached;

    static ZoomImage()
    {
        AffectsRender<ZoomImage>(SourceProperty);
        ZoomProperty.Changed.AddClassHandler<ZoomImage>((image, args) =>
        {
            // Only while attached: the state lives as long as the group's view model, the control doesn't.
            if (image.isAttached)
            {
                if (args.OldValue is ZoomState old)
                {
                    old.PropertyChanged -= image.OnZoomChanged;
                }

                if (args.NewValue is ZoomState current)
                {
                    current.PropertyChanged += image.OnZoomChanged;
                }
            }

            image.InvalidateVisual();
        });
        FocusableProperty.OverrideDefaultValue<ZoomImage>(false);
    }

    /// <summary>
    /// Gets or sets the image.
    /// </summary>
    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the area shown, shared with the other previews.
    /// </summary>
    public ZoomState? Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        isAttached = true;
        if (Zoom is { } zoom)
        {
            zoom.PropertyChanged += OnZoomChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        isAttached = false;
        if (Zoom is { } zoom)
        {
            zoom.PropertyChanged -= OnZoomChanged;
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Source is not { } source || source.Size.Width <= 0 || source.Size.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var fit = FitRect(source.Size);
        var zoom = Zoom;
        var scale = zoom?.Scale ?? 1;
        var viewWidth = source.Size.Width / scale;
        var viewHeight = source.Size.Height / scale;
        var left = ((zoom?.CenterX ?? 0.5) * source.Size.Width) - (viewWidth / 2);
        var top = ((zoom?.CenterY ?? 0.5) * source.Size.Height) - (viewHeight / 2);
        using (context.PushClip(fit))
        {
            context.DrawImage(source, new Rect(left, top, viewWidth, viewHeight), fit);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Zoom is not { } zoom || Source is not { } source || e.Delta.Y == 0)
        {
            return;
        }

        var (ax, ay) = Fraction(e.GetPosition(this), FitRect(source.Size));
        zoom.ZoomAt(e.Delta.Y > 0 ? WheelStep : 1 / WheelStep, ax, ay);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            Zoom?.Reset();
            dragFrom = null;
        }
        else
        {
            dragFrom = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragFrom is not { } from || Zoom is not { } zoom || Source is not { } source)
        {
            return;
        }

        var now = e.GetPosition(this);
        var fit = FitRect(source.Size);
        if (fit.Width > 0 && fit.Height > 0)
        {
            zoom.PanBy((now.X - from.X) / fit.Width, (now.Y - from.Y) / fit.Height);
        }

        dragFrom = now;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        dragFrom = null;
        e.Pointer.Capture(null);
    }

    private static (double X, double Y) Fraction(Point point, Rect fit) =>
        (Math.Clamp((point.X - fit.X) / Math.Max(fit.Width, 1), 0, 1), Math.Clamp((point.Y - fit.Y) / Math.Max(fit.Height, 1), 0, 1));

    // The image fitted into the control, keeping its shape.
    private Rect FitRect(Size image)
    {
        var factor = Math.Min(Bounds.Width / image.Width, Bounds.Height / image.Height);
        var size = new Size(image.Width * factor, image.Height * factor);
        return new Rect((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2, size.Width, size.Height);
    }

    private void OnZoomChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => InvalidateVisual();
}
