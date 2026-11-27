using MediaFileClassifier.Ui.ViewModels.Duplicates;

namespace MediaFileClassifier.Ui.Tests.Duplicates;

/// <summary>
/// Tests for <see cref="ZoomState"/>, the area shared by the previews of zoom compare (E12-S03-T04).
/// </summary>
public sealed class ZoomStateTests
{
    [Fact]
    public void ZoomAt_KeepsThePointUnderThePointerInPlace()
    {
        var zoom = new ZoomState();

        zoom.ZoomAt(2, 0.75, 0.25);

        // The point at 75%/25% of the whole image is still at 75%/25% of the view: centre + (anchor - 0.5) / scale.
        Assert.Equal(2, zoom.Scale);
        Assert.Equal(0.75, zoom.CenterX + ((0.75 - 0.5) / zoom.Scale), 6);
        Assert.Equal(0.25, zoom.CenterY + ((0.25 - 0.5) / zoom.Scale), 6);
    }

    [Fact]
    public void ZoomAt_ClampsBetweenTheWholeImageAndTheMaximum()
    {
        var zoom = new ZoomState();

        zoom.ZoomAt(1000, 0.5, 0.5);
        Assert.Equal(ZoomState.MaxScale, zoom.Scale);

        zoom.ZoomAt(0.0001, 0.5, 0.5);
        Assert.Equal(1, zoom.Scale);
        Assert.Equal(0.5, zoom.CenterX);
    }

    [Fact]
    public void PanBy_MovesThePictureWithThePointer_AndStaysInsideTheImage()
    {
        var zoom = new ZoomState();
        zoom.ZoomAt(4, 0.5, 0.5);

        zoom.PanBy(0.4, 0);
        Assert.Equal(0.5 - 0.1, zoom.CenterX, 6);

        zoom.PanBy(100, -100);
        Assert.Equal(0.5 / 4, zoom.CenterX, 6);
        Assert.Equal(1 - (0.5 / 4), zoom.CenterY, 6);
    }

    [Fact]
    public void Reset_ShowsTheWholeImage()
    {
        var zoom = new ZoomState();
        zoom.ZoomAt(8, 0.2, 0.9);

        zoom.Reset();

        Assert.False(zoom.IsChanged);
        Assert.Equal((1d, 0.5, 0.5), (zoom.Scale, zoom.CenterX, zoom.CenterY));
    }
}
