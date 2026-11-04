using Avalonia;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// The zoom and pan of the details panel preview (E14-S05-T02). <see cref="Zoom"/> is image pixels per screen pixel,
/// so 1 is 100%. Fit shows the whole image and never enlarges it; a zoomed image is kept covering the viewport, and
/// a smaller one centred.
/// </summary>
public sealed class ZoomState
{
    /// <summary>The largest zoom, 800%.</summary>
    public const double MaxZoom = 8;

    /// <summary>The zoom change per mouse wheel notch.</summary>
    public const double WheelStep = 1.25;

    private Size viewport;
    private Size image;
    private double scaling = 1;
    private Point offset;

    /// <summary>
    /// Gets the zoom: image pixels per screen pixel.
    /// </summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>
    /// Gets a value indicating whether the image is fitted to the viewport, so it refits when either changes.
    /// </summary>
    public bool IsFit { get; private set; } = true;

    /// <summary>
    /// Gets the zoom that fits the whole image, at most 100%.
    /// </summary>
    public double FitZoom => image.Width <= 0 || image.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0
        ? 1
        : Math.Min(1, Math.Min(viewport.Width / image.Width, viewport.Height / image.Height) * scaling);

    /// <summary>
    /// Gets where the image is drawn, in device-independent pixels within the viewport.
    /// </summary>
    public Rect ImageRect => new(offset, new Size(image.Width * Scale, image.Height * Scale));

    private double Scale => Zoom / scaling;

    /// <summary>
    /// Sets the viewport and the image, keeping a fitted image fitted and a zoomed one in place.
    /// </summary>
    /// <param name="viewportSize">The viewport, in device-independent pixels.</param>
    /// <param name="imageSize">The image as shown (orientation applied), in pixels.</param>
    /// <param name="renderScaling">The screen's render scaling.</param>
    public void Update(Size viewportSize, Size imageSize, double renderScaling)
    {
        var imageChanged = imageSize != image;
        viewport = viewportSize;
        image = imageSize;
        scaling = renderScaling > 0 ? renderScaling : 1;
        if (IsFit || imageChanged)
        {
            Fit();
        }
        else
        {
            Clamp();
        }
    }

    /// <summary>
    /// Fits the whole image in the viewport.
    /// </summary>
    public void Fit()
    {
        IsFit = true;
        Zoom = FitZoom;
        Clamp();
    }

    /// <summary>
    /// Shows the image at 100%, around the viewport's centre.
    /// </summary>
    public void ActualSize() => ZoomAt(new Point(viewport.Width / 2, viewport.Height / 2), 1 / Zoom);

    /// <summary>
    /// Zooms by a factor, keeping the image point under <paramref name="anchor"/> in place.
    /// </summary>
    /// <param name="anchor">The point that stays put, e.g. the mouse, in viewport coordinates.</param>
    /// <param name="factor">The zoom factor, e.g. <see cref="WheelStep"/> to zoom in.</param>
    public void ZoomAt(Point anchor, double factor)
    {
        var zoom = Math.Clamp(Zoom * factor, Math.Min(FitZoom, 1), MaxZoom);
        if (zoom <= FitZoom)
        {
            Fit();
            return;
        }

        var ratio = zoom / Zoom;
        offset = new Point(anchor.X - ((anchor.X - offset.X) * ratio), anchor.Y - ((anchor.Y - offset.Y) * ratio));
        Zoom = zoom;
        IsFit = false;
        Clamp();
    }

    /// <summary>
    /// Pans a zoomed image, e.g. while dragging.
    /// </summary>
    /// <param name="delta">The movement, in device-independent pixels.</param>
    public void PanBy(Vector delta)
    {
        offset += delta;
        Clamp();
    }

    private void Clamp()
    {
        var width = image.Width * Scale;
        var height = image.Height * Scale;
        offset = new Point(Axis(offset.X, width, viewport.Width), Axis(offset.Y, height, viewport.Height));

        // Smaller than the viewport: centred. Larger: no gap at either edge.
        static double Axis(double position, double length, double available) =>
            length <= available ? (available - length) / 2 : Math.Clamp(position, available - length, 0);
    }
}
