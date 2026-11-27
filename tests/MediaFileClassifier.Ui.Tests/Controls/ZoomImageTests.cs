using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MediaFileClassifier.Ui.Controls;
using MediaFileClassifier.Ui.ViewModels.Duplicates;

namespace MediaFileClassifier.Ui.Tests.Controls;

/// <summary>
/// Tests for <see cref="ZoomImage"/> (E12-S03-T04): previews that share one <see cref="ZoomState"/> move together.
/// </summary>
public sealed class ZoomImageTests
{
    [Fact]
    public Task WheelOnOnePreview_ZoomsEveryPreviewSharingTheState() => TestApp.RunAsync(() =>
    {
        var zoom = new ZoomState();
        var (window, first, second) = Show(zoom);

        window.MouseWheel(new Point(100, 100), new Vector(0, 3));

        // One state drives both previews, so the second shows the same area without being touched.
        Assert.True(zoom.Scale > 1);
        Assert.Same(zoom, first.Zoom);
        Assert.Same(zoom, second.Zoom);
        window.Close();
    });

    [Fact]
    public Task DragPansTheSharedArea_AndDoubleClickResetsIt() => TestApp.RunAsync(() =>
    {
        var zoom = new ZoomState();
        var (window, _, _) = Show(zoom);
        window.MouseWheel(new Point(100, 100), new Vector(0, 5));
        var centreBefore = zoom.CenterX;

        window.MouseDown(new Point(100, 100), MouseButton.Left);
        window.MouseMove(new Point(160, 100));
        window.MouseUp(new Point(160, 100), MouseButton.Left);

        Assert.True(zoom.CenterX < centreBefore);

        // Two quick presses at the same place are a double-click.
        window.MouseDown(new Point(100, 100), MouseButton.Left);
        window.MouseUp(new Point(100, 100), MouseButton.Left);
        window.MouseDown(new Point(100, 100), MouseButton.Left);
        window.MouseUp(new Point(100, 100), MouseButton.Left);

        Assert.Equal(1, zoom.Scale);
        Assert.Equal(0.5, zoom.CenterX);
        window.Close();
    });

    private static (Window Window, ZoomImage First, ZoomImage Second) Show(ZoomState zoom)
    {
        var bitmap = new WriteableBitmap(new PixelSize(400, 400), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        var first = new ZoomImage { Source = bitmap, Zoom = zoom, Width = 200, Height = 200 };
        var second = new ZoomImage { Source = bitmap, Zoom = zoom, Width = 200, Height = 200 };
        var window = new Window
        {
            Width = 420,
            Height = 220,
            Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { first, second } },
        };
        window.Show();
        window.UpdateLayout();
        return (window, first, second);
    }
}
