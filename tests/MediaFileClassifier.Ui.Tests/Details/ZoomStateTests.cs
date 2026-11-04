using Avalonia;
using MediaFileClassifier.Ui.Details;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the preview's zoom and pan (E14-S05-T02): fit, 100%, wheel zoom around the mouse, and panning.
/// </summary>
public sealed class ZoomStateTests
{
    private static readonly Size Viewport = new(320, 200);

    [Theory]
    [InlineData(8000, 6000, 1.0, 0.0333)]
    [InlineData(8000, 6000, 1.5, 0.05)]
    [InlineData(100, 50, 1.0, 1.0)]
    public void Fit_ShowsTheWholeImage_WithoutEnlargingIt(double width, double height, double scaling, double zoom)
    {
        var state = new ZoomState();

        state.Update(Viewport, new Size(width, height), scaling);

        Assert.True(state.IsFit);
        Assert.Equal(zoom, state.Zoom, 3);
        var rect = state.ImageRect;
        Assert.True(rect.Width <= Viewport.Width + 0.001 && rect.Height <= Viewport.Height + 0.001);
        Assert.Equal(Viewport.Width / 2, rect.Center.X, 3);
        Assert.Equal(Viewport.Height / 2, rect.Center.Y, 3);
    }

    [Fact]
    public void ActualSize_ShowsOneImagePixelPerScreenPixel_AroundTheCentre()
    {
        var state = new ZoomState();
        state.Update(Viewport, new Size(8000, 6000), 2);

        state.ActualSize();

        Assert.False(state.IsFit);
        Assert.Equal(1, state.Zoom, 6);
        Assert.Equal(new Size(4000, 3000), state.ImageRect.Size);
        Assert.Equal(160, state.ImageRect.Center.X, 0.0001);
        Assert.Equal(100, state.ImageRect.Center.Y, 0.0001);
    }

    [Fact]
    public void Wheel_ZoomsAroundTheMouse_AndBackToFit()
    {
        var state = new ZoomState();
        state.Update(Viewport, new Size(8000, 6000), 1);
        var mouse = new Point(150, 90);
        var before = ImagePointAt(state, mouse);

        state.ZoomAt(mouse, ZoomState.WheelStep);
        state.ZoomAt(mouse, ZoomState.WheelStep);

        Assert.Equal(before.X, ImagePointAt(state, mouse).X, 0.0001);
        Assert.Equal(before.Y, ImagePointAt(state, mouse).Y, 0.0001);
        state.ZoomAt(mouse, 1 / 100d);
        Assert.True(state.IsFit);
    }

    [Fact]
    public void Zoom_StopsAt800Percent()
    {
        var state = new ZoomState();
        state.Update(Viewport, new Size(8000, 6000), 1);

        state.ZoomAt(default, 1_000_000);

        Assert.Equal(ZoomState.MaxZoom, state.Zoom);
    }

    [Fact]
    public void Pan_MovesAZoomedImage_ButNotPastItsEdges()
    {
        var state = new ZoomState();
        state.Update(Viewport, new Size(800, 600), 1);
        state.ActualSize();
        var start = state.ImageRect.Position;

        state.PanBy(new Vector(-50, 20));
        Assert.Equal(start + new Vector(-50, 20), state.ImageRect.Position);

        state.PanBy(new Vector(10_000, 10_000));
        Assert.Equal(new Point(0, 0), state.ImageRect.Position);
        state.PanBy(new Vector(-10_000, -10_000));
        Assert.Equal(new Point(320 - 800, 200 - 600), state.ImageRect.Position);
    }

    [Fact]
    public void Resize_KeepsAFittedImageFitted_AndANewImageIsFitted()
    {
        var state = new ZoomState();
        state.Update(Viewport, new Size(8000, 6000), 1);

        state.Update(new Size(640, 480), new Size(8000, 6000), 1);
        Assert.Equal(0.08, state.Zoom, 6);

        state.ActualSize();
        state.Update(new Size(640, 480), new Size(4000, 3000), 1);
        Assert.True(state.IsFit);
    }

    private static Point ImagePointAt(ZoomState state, Point point)
    {
        var rect = state.ImageRect;
        return new Point((point.X - rect.X) / rect.Width, (point.Y - rect.Y) / rect.Height);
    }
}
