using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Playback;
using MediaFileClassifier.Ui.Controls;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Tests.Browser;

namespace MediaFileClassifier.Ui.Tests.Controls;

/// <summary>
/// Tests for the video player's controls (E14-S06-T01): the frame buffer between the decoder and the UI thread, the
/// surface that draws it, and the timeline.
/// </summary>
public sealed class VideoControlsTests
{
    [Fact]
    public void FrameBuffer_KeepsOnlyTheNewestFrame_AndSignalsOncePerPresent()
    {
        var buffer = new VideoFrameBuffer();
        var signals = 0;
        buffer.FrameReady += (_, _) => signals++;

        // Two frames before the UI thread gets to them: one signal, and the second frame wins.
        buffer.OnFrame(Frame(4, 2, stride: 20, value: 1));
        buffer.OnFrame(Frame(4, 2, stride: 16, value: 7));

        Assert.Equal(1, signals);
        Assert.Equal(2, buffer.Version);
        Assert.True(buffer.HasFrame);
    }

    [Fact]
    public Task FrameBuffer_Present_CopiesTheRowsWithoutTheStridePadding_AndReusesTheBitmap() => TestApp.RunAsync(() =>
    {
        var buffer = new VideoFrameBuffer();
        buffer.OnFrame(Frame(4, 2, stride: 20, value: 9));

        var bitmap = buffer.Present(null);

        Assert.NotNull(bitmap);
        Assert.Equal(new PixelSize(4, 2), bitmap.PixelSize);
        var copy = new byte[2 * 24];
        buffer.CopyTo(copy, 24);
        Assert.All(copy.AsSpan(24, 16).ToArray(), value => Assert.Equal(9, value));
        Assert.All(copy.AsSpan(40, 8).ToArray(), value => Assert.Equal(0, value));

        buffer.OnFrame(Frame(4, 2, stride: 16, value: 3));
        Assert.Same(bitmap, buffer.Present(bitmap));

        buffer.Clear();
        Assert.Null(buffer.Present(bitmap));
        Assert.False(buffer.HasFrame);
    });

    [Fact]
    public void FrameBuffer_IgnoresFramesThatDontFitTheirSize()
    {
        var buffer = new VideoFrameBuffer();

        buffer.OnFrame(new VideoFrame(new byte[10], 4, 2, 16));

        Assert.False(buffer.HasFrame);
    }

    [Fact]
    public Task Surface_DrawsAFrameOnceItArrives_AndNothingAfterClear() => TestApp.RunAsync(() =>
    {
        var buffer = new VideoFrameBuffer();
        var surface = new VideoSurface { Source = buffer };
        var window = Show(surface);

        Assert.False(surface.HasFrame);
        buffer.OnFrame(Frame(8, 6, stride: 32, value: 5));
        Pump.Until(() => surface.HasFrame);
        buffer.Clear();
        Pump.Until(() => !surface.HasFrame);
        window.Close();
    });

    [Fact]
    public Task Timeline_ClickAndDrag_SeekToTheTimeUnderThePointer() => TestApp.RunAsync(() =>
    {
        var seeks = new List<TimeSpan>();
        var timeline = new VideoTimeline { Duration = TimeSpan.FromSeconds(100), SeekCommand = new RelayCommand<TimeSpan>(seeks.Add) };
        var window = Show(timeline, width: 212);
        var y = timeline.Bounds.Height / 2;

        // The track runs from x = 6 to 206, inset by the thumb.
        window.MouseDown(new Point(106, y), MouseButton.Left);
        window.MouseMove(new Point(156, y));
        window.MouseMove(new Point(500, y));
        window.MouseUp(new Point(500, y), MouseButton.Left);
        window.MouseMove(new Point(30, y));

        Assert.Equal([TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(75), TimeSpan.FromSeconds(100)], seeks);
        Assert.True(timeline.IsFocused);
        window.Close();
    });

    [Theory]
    [InlineData(Key.Right, 45)]
    [InlineData(Key.Left, 35)]
    [InlineData(Key.PageUp, 70)]
    [InlineData(Key.PageDown, 10)]
    [InlineData(Key.Home, 0)]
    [InlineData(Key.End, 100)]
    public Task Timeline_Keys_StepAndJump(Key key, double seconds) => TestApp.RunAsync(() =>
    {
        TimeSpan? seek = null;
        var timeline = new VideoTimeline
        {
            Duration = TimeSpan.FromSeconds(100),
            Position = TimeSpan.FromSeconds(40),
            SeekCommand = new RelayCommand<TimeSpan>(time => seek = time),
        };
        var window = Show(timeline);
        timeline.Focus();

        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);

        Assert.Equal(TimeSpan.FromSeconds(seconds), seek);
        window.Close();
    });

    [Fact]
    public Task Timeline_ClickNearAMarker_SeeksExactlyToIt_AndElsewhereToThePointer() => TestApp.RunAsync(() =>
    {
        var seeks = new List<TimeSpan>();
        var timeline = new VideoTimeline
        {
            Duration = TimeSpan.FromSeconds(100),
            Markers = [new TimelineMarker(TimeSpan.FromSeconds(30.25)), new TimelineMarker(TimeSpan.FromSeconds(80))],
            SeekCommand = new RelayCommand<TimeSpan>(seeks.Add),
        };
        var window = Show(timeline, width: 212);
        var y = timeline.Bounds.Height / 2;

        // 30.25 s is at x = 66.5; 4 px off still hits it, 10 px off doesn't.
        window.MouseDown(new Point(70.5, y), MouseButton.Left);
        window.MouseUp(new Point(70.5, y), MouseButton.Left);
        window.MouseDown(new Point(106, y), MouseButton.Left);
        window.MouseUp(new Point(106, y), MouseButton.Left);

        Assert.Equal([TimeSpan.FromSeconds(30.25), TimeSpan.FromSeconds(50)], seeks);
        Assert.Equal(TimeSpan.FromSeconds(80), timeline.MarkerAt(timeline.XOf(TimeSpan.FromSeconds(80)) + VideoTimeline.MarkerHitRadius)?.Time);
        Assert.Null(timeline.MarkerAt(timeline.XOf(TimeSpan.FromSeconds(80)) + VideoTimeline.MarkerHitRadius + 1));
        window.Close();
    });

    [Theory]
    [InlineData(40, Key.Right, 80.0)]
    [InlineData(40, Key.Left, 30.25)]
    [InlineData(30, Key.Left, 10.0)]
    [InlineData(30, Key.Right, 80.0)]
    [InlineData(90, Key.Right, null)]
    public Task Timeline_CtrlArrows_GoToTheNextOrPreviousMarker(double position, Key key, double? expected) => TestApp.RunAsync(() =>
    {
        TimeSpan? seek = null;
        var timeline = new VideoTimeline
        {
            Duration = TimeSpan.FromSeconds(100),
            Position = TimeSpan.FromSeconds(position),
            Markers = [new TimelineMarker(TimeSpan.FromSeconds(80)), new TimelineMarker(TimeSpan.FromSeconds(10)), new TimelineMarker(TimeSpan.FromSeconds(30.25))],
            SeekCommand = new RelayCommand<TimeSpan>(time => seek = time),
        };
        var window = Show(timeline);
        timeline.Focus();

        window.KeyPress(key, RawInputModifiers.Control, PhysicalKey.None, null);

        Assert.Equal(expected is { } seconds ? TimeSpan.FromSeconds(seconds) : null, seek);
        window.Close();
    });

    [Fact]
    public Task Timeline_WithoutALength_DoesNotSeek() => TestApp.RunAsync(() =>
    {
        var seeks = 0;
        var timeline = new VideoTimeline { SeekCommand = new RelayCommand<TimeSpan>(_ => seeks++) };
        var window = Show(timeline);

        window.MouseDown(new Point(50, timeline.Bounds.Height / 2), MouseButton.Left);
        timeline.Focus();
        window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.None, null);

        Assert.Equal(0, seeks);
        window.Close();
    });

    private static VideoFrame Frame(int width, int height, int stride, byte value)
    {
        var pixels = new byte[stride * height];
        Array.Fill(pixels, value);
        return new VideoFrame(pixels, width, height, stride);
    }

    private static Window Show(Control control, double width = 200)
    {
        var window = new Window
        {
            Width = width,
            Height = 100,
            Content = new StackPanel { Children = { control } },
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
