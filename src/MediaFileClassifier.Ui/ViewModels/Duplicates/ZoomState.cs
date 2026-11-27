using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaFileClassifier.Ui.ViewModels.Duplicates;

/// <summary>
/// The area every preview of a group shows in zoom compare (E12-S03-T04). It is shared by all members and given in
/// fractions of the image, so images of different resolutions show the same part of the picture.
/// </summary>
public sealed partial class ZoomState : ObservableObject
{
    /// <summary>
    /// The largest zoom factor.
    /// </summary>
    public const double MaxScale = 16;

    /// <summary>
    /// Gets or sets the zoom factor: 1 shows the whole image.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial double Scale { get; set; } = 1;

    /// <summary>
    /// Gets or sets the horizontal centre of the visible area, from 0 (left edge) to 1 (right edge).
    /// </summary>
    [ObservableProperty]
    public partial double CenterX { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the vertical centre of the visible area, from 0 (top edge) to 1 (bottom edge).
    /// </summary>
    [ObservableProperty]
    public partial double CenterY { get; set; } = 0.5;

    /// <summary>
    /// Gets a value indicating whether the view is zoomed in or panned away from the start.
    /// </summary>
    public bool IsChanged => Scale > 1;

    /// <summary>
    /// Zooms by a factor and keeps the picture point under the pointer in place.
    /// </summary>
    /// <param name="factor">Greater than 1 zooms in, less than 1 zooms out.</param>
    /// <param name="anchorX">The pointer's horizontal position in the view, from 0 to 1.</param>
    /// <param name="anchorY">The pointer's vertical position in the view, from 0 to 1.</param>
    public void ZoomAt(double factor, double anchorX, double anchorY)
    {
        var scale = Math.Clamp(Scale * factor, 1, MaxScale);
        var pointX = CenterX + ((anchorX - 0.5) / Scale);
        var pointY = CenterY + ((anchorY - 0.5) / Scale);
        Scale = scale;
        CenterX = pointX - ((anchorX - 0.5) / scale);
        CenterY = pointY - ((anchorY - 0.5) / scale);
        Clamp();
    }

    /// <summary>
    /// Moves the picture with the pointer.
    /// </summary>
    /// <param name="deltaX">The pointer's horizontal move, as a fraction of the view's width.</param>
    /// <param name="deltaY">The pointer's vertical move, as a fraction of the view's height.</param>
    public void PanBy(double deltaX, double deltaY)
    {
        CenterX -= deltaX / Scale;
        CenterY -= deltaY / Scale;
        Clamp();
    }

    /// <summary>
    /// Shows the whole image again.
    /// </summary>
    public void Reset()
    {
        Scale = 1;
        CenterX = 0.5;
        CenterY = 0.5;
    }

    // The visible area stays inside the image.
    private void Clamp()
    {
        var half = 0.5 / Scale;
        CenterX = Math.Clamp(CenterX, half, 1 - half);
        CenterY = Math.Clamp(CenterY, half, 1 - half);
    }
}
