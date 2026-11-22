using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Playback;
using MediaFileClassifier.Infrastructure.Playback;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Testing.Timing;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Playback;

/// <summary>
/// Tests for the LibVLC preview player on the frames route (E14-S06-T01, D070), against the sample clips and the
/// trimmed LibVLC that the tests ship like the app.
/// </summary>
public sealed class LibVlcPreviewPlayerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly LibVlcRuntime runtime = new();
    private readonly RecordingSink sink = new();

    public void Dispose()
    {
        runtime.Dispose();
    }

    [Fact]
    public async Task Probe_LoadsTheTrimmedLibVlc_AndReportsVersion3()
    {
        var probe = new LibVlcProbe(runtime);

        var status = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.True(status.IsAvailable, status.Detail);
        Assert.StartsWith("3.", status.Version, StringComparison.Ordinal);
        Assert.Equal([KnownCapabilities.VideoPlayback], probe.Capabilities);
    }

    [Theory]
    [InlineData("video-short.mp4", 320, 240, 2.0)]
    [InlineData("video-hevc.mp4", 160, 120, 1.0)]
    public async Task Open_H264AndH265_PausesOnTheFirstFrame_ThenPlaysToTheEnd_AndPlaysAgainFromTheStart(string file, int width, int height, double seconds)
    {
        using var player = CreatePlayer();

        player.Open(Path.Combine(SampleMedia.Directory, file), TimeSpan.Zero, play: false);

        await WaitForAsync(() => player.State == PlaybackState.Paused);
        Assert.Equal((width, height), sink.LastSize);
        var framesWhilePaused = sink.Frames;
        var pausedAt = player.Position;
        await Task.Delay(300, TestContext.Current.CancellationToken);

        // LibVLC may draw the paused picture again, but it doesn't move on.
        Assert.Equal(pausedAt, player.Position);
        Assert.Equal(PlaybackState.Paused, player.State);
        await WaitForAsync(() => player.Duration > TimeSpan.Zero);
        Assert.InRange(player.Duration.TotalSeconds, seconds - 0.2, seconds + 0.2);

        player.Play();
        await WaitForAsync(() => player.State == PlaybackState.Playing);
        await WaitForAsync(() => player.State == PlaybackState.Ended);
        Assert.True(sink.Frames > framesWhilePaused + 5, $"Only {sink.Frames} frames.");
        Assert.Equal(player.Duration, player.Position);

        player.Play();
        await WaitForAsync(() => player.State == PlaybackState.Playing);
        Assert.True(player.Position < player.Duration);
    }

    [Fact]
    public async Task Seek_WhilePaused_ShowsTheFrameThere_AndStaysPaused()
    {
        using var player = CreatePlayer();
        player.Open(SampleMedia.ShortVideo, TimeSpan.Zero, play: false);
        await WaitForAsync(() => player.State == PlaybackState.Paused && player.Duration > TimeSpan.Zero);
        var frames = sink.Frames;

        player.Seek(TimeSpan.FromSeconds(1.2));

        Assert.Equal(TimeSpan.FromSeconds(1.2), player.Position);
        await WaitForAsync(() => sink.Frames > frames);
        Assert.Equal(PlaybackState.Paused, player.State);
        Assert.InRange(player.Position.TotalSeconds, 0.8, 1.3);
    }

    [Fact]
    public async Task Seek_PastTheEnd_IsClampedToTheLength()
    {
        using var player = CreatePlayer();
        player.Open(SampleMedia.ShortVideo, TimeSpan.Zero, play: false);
        await WaitForAsync(() => player.State == PlaybackState.Paused && player.Duration > TimeSpan.Zero);

        player.Seek(TimeSpan.FromMinutes(5));

        Assert.Equal(player.Duration, player.Position);
    }

    [Fact]
    public async Task Open_AtATime_StartsThere()
    {
        using var player = CreatePlayer();

        player.Open(SampleMedia.ShortVideo, TimeSpan.FromSeconds(1), play: false);

        await WaitForAsync(() => player.State == PlaybackState.Paused);
        Assert.InRange(player.Position.TotalSeconds, 0.7, 1.1);
    }

    [Fact]
    public async Task Open_AFileWithNoVideo_FailsAfterTheFirstFrameTimeout()
    {
        using var player = CreatePlayer();

        player.Open(SampleMedia.AudioOnlyMp4, TimeSpan.Zero, play: false);

        await WaitForAsync(() => player.State == PlaybackState.Failed, LibVlcPreviewPlayer.FirstFrameTimeout + Timeout);
        Assert.NotNull(player.Error);
        Assert.Equal(0, sink.Frames);
    }

    [Fact]
    public async Task Open_AMissingFile_Fails()
    {
        using var player = CreatePlayer();

        player.Open(Path.Combine(SampleMedia.Directory, "no-such-file.mp4"), TimeSpan.Zero, play: false);

        await WaitForAsync(() => player.State == PlaybackState.Failed, LibVlcPreviewPlayer.FirstFrameTimeout + Timeout);
    }

    [Fact]
    public async Task Close_StopsAndClearsTheFrame_AndPlayDoesNothingAfterwards()
    {
        using var player = CreatePlayer();
        player.Open(SampleMedia.ShortVideo, TimeSpan.Zero, play: true);
        await WaitForAsync(() => player.State == PlaybackState.Playing);

        player.Close();
        await WaitForAsync(() => player.State == PlaybackState.Closed);
        player.Play();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackState.Closed, player.State);
        Assert.True(sink.Cleared > 0);
        Assert.Equal(TimeSpan.Zero, player.Duration);
    }

    [Fact]
    public async Task Open_AnotherFile_ReplacesTheFirst()
    {
        using var player = CreatePlayer();
        player.Open(SampleMedia.ShortVideo, TimeSpan.Zero, play: true);

        player.Open(SampleMedia.HevcVideo, TimeSpan.Zero, play: false);

        await WaitForAsync(() => player.State == PlaybackState.Paused && player.Duration > TimeSpan.Zero);
        Assert.Equal((160, 120), sink.LastSize);
        Assert.InRange(player.Duration.TotalSeconds, 0.8, 1.2);
    }

    [Fact]
    public void VolumeAndMute_AreClampedAndKept()
    {
        using var player = CreatePlayer();

        player.Volume = 150;
        player.IsMuted = true;

        Assert.Equal(100, player.Volume);
        Assert.True(player.IsMuted);
        player.Volume = -5;
        Assert.Equal(0, player.Volume);
    }

    [Fact]
    public async Task Frames_AreScaledDownToTheLargestSize()
    {
        using var player = new LibVlcPreviewPlayer(runtime.GetInstance(), sink, 160, 90, NullLogger<LibVlcPreviewPlayer>.Instance);

        player.Open(SampleMedia.ShortVideo, TimeSpan.Zero, play: false);

        await WaitForAsync(() => player.State == PlaybackState.Paused);
        Assert.Equal((120, 90), sink.LastSize);
    }

    [Fact]
    public async Task Dispose_WhilePlaying_ReturnsQuickly_AndLaterRequestsAreIgnored()
    {
        var player = CreatePlayer();
        player.Open(SampleMedia.ShortVideo, TimeSpan.Zero, play: true);
        await WaitForAsync(() => player.State == PlaybackState.Playing);

        var stopwatch = Stopwatch.StartNew();
        player.Dispose();
        player.Dispose();
        player.Play();
        player.Seek(TimeSpan.FromSeconds(1));
        stopwatch.Stop();

        // A dispose that deadlocks with LibVLC's threads never returns; under 3 s on an idle machine (MFC_PERF_TESTS=1,
        // TestPerformance).
        Assert.True(stopwatch.Elapsed < Timeout, $"Disposing took {stopwatch.Elapsed}.");
        if (TestPerformance.Enabled)
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Disposing took {stopwatch.Elapsed}.");
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Timeout);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the player.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private LibVlcPreviewPlayer CreatePlayer() =>
        new(runtime.GetInstance(), sink, 1280, 720, NullLogger<LibVlcPreviewPlayer>.Instance);

    /// <summary>
    /// Counts the frames and remembers the last frame's size.
    /// </summary>
    private sealed class RecordingSink : IVideoFrameSink
    {
        private int frames;
        private int cleared;
        private long lastSize;

        public int Frames => Volatile.Read(ref frames);

        public int Cleared => Volatile.Read(ref cleared);

        public (int Width, int Height) LastSize
        {
            get
            {
                var size = Interlocked.Read(ref lastSize);
                return ((int)(size >> 32), (int)size);
            }
        }

        public void OnFrame(VideoFrame frame)
        {
            Assert.True(frame.Pixels.Length >= frame.Stride * frame.Height);
            Interlocked.Exchange(ref lastSize, ((long)frame.Width << 32) | (uint)frame.Height);
            Interlocked.Increment(ref frames);
        }

        public void Clear() => Interlocked.Increment(ref cleared);
    }
}
