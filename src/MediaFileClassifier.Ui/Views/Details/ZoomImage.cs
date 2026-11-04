using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Details;

namespace MediaFileClassifier.Ui.Views.Details;

/// <summary>
/// Shows an image that can be zoomed with the mouse wheel and panned by dragging (E14-S05-T02), with EXIF
/// orientation applied. The bitmap may be smaller than the image, e.g. a screen-sized decode, and is stretched to
/// <see cref="ImageSize"/>, so a sharper bitmap can replace it without moving anything.
/// </summary>
public sealed class ZoomImage : Control
{
    /// <summary>Defines the <see cref="Source"/> property.</summary>
    public static readonly StyledProperty<Bitmap?> SourceProperty = AvaloniaProperty.Register<ZoomImage, Bitmap?>(nameof(Source));

    /// <summary>Defines the <see cref="ImageSize"/> property.</summary>
    public static readonly StyledProperty<Size> ImageSizeProperty = AvaloniaProperty.Register<ZoomImage, Size>(nameof(ImageSize));

    /// <summary>Defines the <see cref="Orientation"/> property.</summary>
    public static readonly StyledProperty<ImageOrientation> OrientationProperty =
        AvaloniaProperty.Register<ZoomImage, ImageOrientation>(nameof(Orientation), ImageOrientation.Normal);

    /// <summary>Defines the <see cref="Zoom"/> property.</summary>
    public static readonly DirectProperty<ZoomImage, double> ZoomProperty =
        AvaloniaProperty.RegisterDirect<ZoomImage, double>(nameof(Zoom), image => image.Zoom);

    private readonly ZoomState state = new();
    private Point? dragFrom;
    private double zoom = 1;

    static ZoomImage()
    {
        AffectsRender<ZoomImage>(SourceProperty, ImageSizeProperty, OrientationProperty);
        FocusableProperty.OverrideDefaultValue<ZoomImage>(true);
        ClipToBoundsProperty.OverrideDefaultValue<ZoomImage>(true);
    }

    /// <summary>
    /// Gets or sets the bitmap, stored as in the file (orientation not applied).
    /// </summary>
    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the image's full size in pixels, orientation applied; empty to use the bitmap's size.
    /// </summary>
    public Size ImageSize
    {
        get => GetValue(ImageSizeProperty);
        set => SetValue(ImageSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets how the bitmap must be turned to show it upright.
    /// </summary>
    public ImageOrientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets the zoom: image pixels per screen pixel, so 1 is 100%.
    /// </summary>
    public double Zoom
    {
        get => zoom;
        private set => SetAndRaise(ZoomProperty, ref zoom, value);
    }

    /// <summary>
    /// Fits the whole image.
    /// </summary>
    public void Fit()
    {
        state.Fit();
        Changed();
    }

    /// <summary>
    /// Shows the image at 100%.
    /// </summary>
    public void ActualSize()
    {
        state.ActualSize();
        Changed();
    }

    /// <summary>
    /// Gets the image's size, orientation applied: <see cref="ImageSize"/>, or the bitmap's.
    /// </summary>
    /// <returns>The size, or empty without an image.</returns>
    public Size ShownImageSize()
    {
        if (ImageSize is { Width: > 0, Height: > 0 } size)
        {
            return size;
        }

        if (Source is not { } source)
        {
            return default;
        }

        var pixels = source.PixelSize;
        return IsTurned(Orientation) ? new Size(pixels.Height, pixels.Width) : new Size(pixels.Width, pixels.Height);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Source is not { } source)
        {
            return;
        }

        UpdateState();
        var target = state.ImageRect;
        var bitmapSize = source.Size;

        // Map the stored bitmap to a unit square, turn it upright there, then stretch it over the target.
        var toUnit = Matrix.CreateScale(1 / bitmapSize.Width, 1 / bitmapSize.Height);
        var toTarget = Matrix.CreateScale(target.Width, target.Height) * Matrix.CreateTranslation(target.X, target.Y);
        using (context.PushTransform(toUnit * OrientationMatrix(Orientation) * toTarget))
        {
            context.DrawImage(source, new Rect(bitmapSize));
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == ImageSizeProperty || change.Property == OrientationProperty)
        {
            UpdateState();
            Zoom = state.Zoom;
        }
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        UpdateState(size);
        Zoom = state.Zoom;
        return size;
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Source is null || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        state.ZoomAt(e.GetPosition(this), Math.Pow(ZoomState.WheelStep, e.Delta.Y));
        Changed();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
        if (e.ClickCount == 2)
        {
            // Double-click switches between fit and 100%.
            if (state.IsFit)
            {
                state.ZoomAt(point.Position, 1 / state.Zoom);
            }
            else
            {
                state.Fit();
            }

            Changed();
        }
        else if (!state.IsFit)
        {
            dragFrom = point.Position;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragFrom is not { } from)
        {
            return;
        }

        var position = e.GetPosition(this);
        state.PanBy(position - from);
        dragFrom = position;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag();
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    /// <summary>
    /// Gets the transform, within a unit square, that turns a stored image upright (EXIF orientation 1–8).
    /// </summary>
    /// <param name="orientation">The orientation.</param>
    /// <returns>The transform.</returns>
    internal static Matrix OrientationMatrix(ImageOrientation orientation) => orientation switch
    {
        // Matrix(m11, m12, m21, m22, m31, m32): x' = x*m11 + y*m21 + m31, y' = x*m12 + y*m22 + m32.
        ImageOrientation.FlipHorizontal => new Matrix(-1, 0, 0, 1, 1, 0),
        ImageOrientation.Rotate180 => new Matrix(-1, 0, 0, -1, 1, 1),
        ImageOrientation.FlipVertical => new Matrix(1, 0, 0, -1, 0, 1),
        ImageOrientation.Transpose => new Matrix(0, 1, 1, 0, 0, 0),
        ImageOrientation.Rotate90 => new Matrix(0, 1, -1, 0, 1, 0),
        ImageOrientation.Transverse => new Matrix(0, -1, -1, 0, 1, 1),
        ImageOrientation.Rotate270 => new Matrix(0, -1, 1, 0, 0, 1),
        _ => Matrix.Identity,
    };

    private static bool IsTurned(ImageOrientation orientation) => orientation >= ImageOrientation.Transpose;

    private void UpdateState(Size? bounds = null) =>
        state.Update(bounds ?? Bounds.Size, ShownImageSize(), TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);

    private void Changed()
    {
        Zoom = state.Zoom;
        InvalidateVisual();
    }

    private void EndDrag()
    {
        dragFrom = null;
        Cursor = null;
    }
}
