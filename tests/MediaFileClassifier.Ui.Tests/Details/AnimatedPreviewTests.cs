using Avalonia;
using Avalonia.Labs.AnimatedImage;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Animated GIF and WebP playback in the preview (E14-S12-T01): the frame timeline, the player's play, pause and step,
/// and the loader with its decoded-size limit.
/// </summary>
public sealed class AnimatedPreviewTests : IDisposable
{
    private readonly TemporaryFolder temp = new();

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Timeline_VariableDelays_GiveEachFrameItsOwnTime()
    {
        var timeline = new AnimationTimeline([1000, 1000, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 1000]);

        Assert.Equal(13, timeline.FrameCount);
        Assert.Equal(0, timeline.IndexAt(TimeSpan.FromMilliseconds(999)));
        Assert.Equal(1, timeline.IndexAt(TimeSpan.FromMilliseconds(1000)));
        Assert.Equal(2, timeline.IndexAt(TimeSpan.FromMilliseconds(2000)));
        Assert.Equal(3, timeline.IndexAt(TimeSpan.FromMilliseconds(2100)));
        Assert.Equal(11, timeline.IndexAt(TimeSpan.FromMilliseconds(2999)));
        Assert.Equal(12, timeline.IndexAt(TimeSpan.FromMilliseconds(3000)));
        Assert.Equal(TimeSpan.FromMilliseconds(2100), timeline.StartOf(3));

        // A second loop.
        Assert.Equal(0, timeline.IndexAt(TimeSpan.FromMilliseconds(4000)));
        Assert.Equal(3, timeline.IndexAt(TimeSpan.FromMilliseconds(6100)));
    }

    [Fact]
    public void Timeline_ZeroDelaysAndOneFrame_DontStallOrDivideByZero()
    {
        var zero = new AnimationTimeline([0, 0, 0]);
        Assert.Equal(TimeSpan.FromMilliseconds(3 * AnimationTimeline.MinDelay), zero.Duration);
        Assert.Equal(1, zero.IndexAt(TimeSpan.FromMilliseconds(1)));

        Assert.Equal(0, new AnimationTimeline([100]).IndexAt(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, new AnimationTimeline([]).IndexAt(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public Task Player_PausesOnTheFrameThatIsShowing_ByTheFramesDelays() => TestApp.RunAsync(() =>
    {
        var clock = new ManualClock();
        using var player = Player(clock, [100, 100, 200, 100]);

        Assert.True(player.IsPlaying);
        clock.Advance(250);
        player.Pause();

        Assert.False(player.IsPlaying);
        Assert.Equal(2, player.FrameIndex);
        Assert.Equal("Frame 3 of 4", player.FrameText);
        Assert.NotNull(player.CurrentFrame);

        // Paused time doesn't count: playing again carries on from the shown frame.
        clock.Advance(10_000);
        player.Play();
        clock.Advance(210);
        player.Pause();
        Assert.Equal(3, player.FrameIndex);
    });

    [Fact]
    public Task Player_StepsFrameByFrame_PausingFirst_AndWrapsAtBothEnds() => TestApp.RunAsync(() =>
    {
        var clock = new ManualClock();
        using var player = Player(clock, [100, 100, 100]);

        player.StepForward();
        Assert.False(player.IsPlaying);
        Assert.Equal(1, player.FrameIndex);

        player.StepForward();
        player.StepForward();
        Assert.Equal(0, player.FrameIndex);

        player.StepBack();
        Assert.Equal(2, player.FrameIndex);
        player.StepBack();
        Assert.Equal(1, player.FrameIndex);
    });

    [Fact]
    public Task Player_PlayAfterAStep_ContinuesFromThatFrame_WithRotatedSource() => TestApp.RunAsync(() =>
    {
        var clock = new ManualClock();
        using var player = Player(clock, [100, 200, 300]);
        var original = player.PlaybackSource;

        player.StepForward();
        player.Play();

        Assert.True(player.IsPlaying);
        Assert.NotSame(original, player.PlaybackSource);
        Assert.Equal([200, 300, 100], player.PlaybackSource!.Delays);

        // Stepping and replaying again keeps the original frames usable.
        clock.Advance(250);
        player.Pause();
        Assert.Equal(2, player.FrameIndex);
        Assert.Equal(2, player.CurrentFrame!.PixelSize.Width);
    });

    [Fact]
    public Task Player_Dispose_StopsPlaybackAndReleasesTheFrames() => TestApp.RunAsync(() =>
    {
        var player = Player(new ManualClock(), [100, 100]);

        player.Dispose();

        Assert.False(player.IsPlaying);
        Assert.Null(player.PlaybackSource);
        Assert.Null(player.CurrentFrame);
    });

    [Fact]
    public void DecodedAnimationBytes_IsWidthTimesHeightTimesFourPerFrame_AndTheLimitIs256MB()
    {
        Assert.Equal(320L * 240 * 4 * 30, PreviewLoader.DecodedAnimationBytes(320, 240, 30));
        Assert.Equal(1920L * 1080 * 4 * 300, PreviewLoader.DecodedAnimationBytes(1920, 1080, 300));
        Assert.Equal(256L * 1024 * 1024, PreviewLoader.MaxAnimationBytes);
        Assert.True(PreviewLoader.DecodedAnimationBytes(320, 240, 30) < PreviewLoader.MaxAnimationBytes);
        Assert.True(PreviewLoader.DecodedAnimationBytes(1920, 1080, 300) > PreviewLoader.MaxAnimationBytes);
    }

    [Theory]
    [InlineData("gif-variable-delays.gif", 13)]
    [InlineData("webp-animated.webp", 0)]
    public Task Loader_LoadsAnimatedFilesWithTheirOwnDelays(string file, int frames) => TestApp.RunAsync(() =>
    {
        var loader = Loader(Path.Combine(SampleMedia.Directory, file));

        var task = loader.LoadAnimationAsync(Details(file), TestContext.Current.CancellationToken);
        Pump.Until(() => task.IsCompleted);
        var loaded = task.Result;

        Assert.NotNull(loaded?.Animation);
        using var animation = (IDisposable)loaded.Animation;
        Assert.False(loaded.IsTooLarge);
        Assert.True(loaded.Animation.FrameCount > 1);

        // The frame bitmaps the preview shows when paused (headless bitmaps all report 1x1).
        Assert.Equal(loaded.Animation.FrameCount, loaded.Animation.Frames.Count);
        Assert.Equal(loaded.Animation.FrameCount, loaded.Animation.Delays.Count);
        if (frames > 0)
        {
            Assert.Equal(frames, loaded.Animation.FrameCount);
            Assert.Equal(1000, loaded.Animation.Delays[0]);
            Assert.Equal(100, loaded.Animation.Delays[5]);
            Assert.Equal(1000, loaded.Animation.Delays[12]);
        }
    });

    [Fact]
    public Task Loader_OneFrameFile_IsNotAnAnimation() => TestApp.RunAsync(() =>
    {
        var loader = Loader(SampleMedia.StaticGif);

        var task = loader.LoadAnimationAsync(Details("gif-static.gif"), TestContext.Current.CancellationToken);
        Pump.Until(() => task.IsCompleted);

        Assert.Null(task.Result);
    });

    [Fact]
    public Task Loader_AboveTheLimit_ReportsTheSizeAndDecodesNothing() => TestApp.RunAsync(() =>
    {
        // Two 1x1 frames on a 16000x16000 screen: tiny on disk, 2 GB decoded.
        var path = temp.Combine("huge.gif");
        File.WriteAllBytes(path, HugeGif(16000, 16000));
        var loader = Loader(path);

        var task = loader.LoadAnimationAsync(Details("huge.gif"), TestContext.Current.CancellationToken);
        Pump.Until(() => task.IsCompleted);
        var loaded = task.Result;

        Assert.NotNull(loaded);
        Assert.True(loaded.IsTooLarge);
        Assert.Null(loaded.Animation);
        Assert.Equal(16000L * 16000 * 4 * 2, loaded.DecodedBytes);
    });

    [Fact]
    public Task Loader_CorruptFile_Throws() => TestApp.RunAsync(() =>
    {
        var path = temp.Combine("broken.gif");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var loader = Loader(path);

        var task = loader.LoadAnimationAsync(Details("broken.gif"), TestContext.Current.CancellationToken);
        Pump.Until(() => task.IsCompleted);

        Assert.True(task.IsFaulted);
        Assert.IsType<InvalidDataException>(task.Exception!.InnerException);
    });

    private static AnimatedPreviewViewModel Player(ManualClock clock, int[] delays)
    {
        var frames = delays.Select(_ => (Bitmap)new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul)).ToArray();
        return new AnimatedPreviewViewModel(IAnimatedBitmap.Load(frames, delays), clock, Localizer.Current);
    }

    private static PreviewLoader Loader(string path)
    {
        var thumbnails = new ThumbnailLoader(new FakeBrowserSession(), new FakeThumbnailCache(), new ThumbnailBitmapCache());
        return new PreviewLoader(thumbnails, new PathFiles(path), new SkiaPreviewDecoder());
    }

    private ItemDetails Details(string file)
    {
        var id = Id<MediaItem>.New();
        var location = new ItemLocation(id, new FileLocation(new FolderHandle(temp.Path, "Photos"), file), file, "Photos", ItemAvailability.Available);
        return new ItemDetails(
            id, file, Path.GetExtension(file).TrimStart('.'), MediaType.AnimatedImage, 1000, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, MediaItemStatus.Indexed, null, location, null, null, null, []);
    }

    internal static byte[] HugeGif(ushort width, ushort height)
    {
        using var stream = new MemoryStream();
        stream.Write("GIF89a"u8);
        stream.Write([(byte)(width & 0xFF), (byte)(width >> 8), (byte)(height & 0xFF), (byte)(height >> 8), 0x80, 0, 0]);
        stream.Write([0, 0, 0, 255, 255, 255]);
        for (var frame = 0; frame < 2; frame++)
        {
            // Graphic control extension (10 ms), then a 1x1 image.
            stream.Write([0x21, 0xF9, 4, 0, 1, 0, 0, 0]);
            stream.Write([0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0]);
            stream.Write([2, 2, 0x44, 0x01, 0]);
        }

        stream.WriteByte(0x3B);
        return stream.ToArray();
    }

    private sealed class ManualClock : TimeProvider
    {
        private long milliseconds;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => milliseconds;

        public void Advance(long by) => milliseconds += by;
    }

    private sealed class PathFiles(string path) : IMediaFileSource
    {
        public Task<Stream> OpenReadAsync(FileLocation file, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(File.OpenRead(path));

        public IAsyncEnumerable<MediaFileStat> EnumerateFilesAsync(FolderHandle folder, string relativeDirectory = "", CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<FolderScanEntry> ScanAsync(FolderHandle folder, FolderScanOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MediaFileStat?> GetStatAsync(FileLocation file, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsAvailableAsync(FolderHandle folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public string? GetPathInside(FolderHandle folder, FolderHandle parent) => throw new NotSupportedException();
    }
}
