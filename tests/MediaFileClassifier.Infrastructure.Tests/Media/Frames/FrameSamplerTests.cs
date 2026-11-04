using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Infrastructure.Media.Frames;
using MediaFileClassifier.Testing.Media;
using Microsoft.Extensions.DependencyInjection;
using static MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails.ThumbnailTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Frames;

/// <summary>
/// Tests for <see cref="FrameSampler"/> (E06-S02-T02, T03) on the sample media. Video tests skip where FFmpeg isn't
/// installed, such as on CI.
/// </summary>
public sealed class FrameSamplerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SampleAsync_StaticCameraVideo_KeepsFewKeyframes()
    {
        RequireFfmpeg();

        var sample = await SampleAsync(SampleMedia.StaticCameraVideo);

        Assert.Empty(sample.Warnings);
        Assert.Equal(16, sample.FramesRead);
        Assert.InRange(sample.Frames.Count, 1, 3);
    }

    [Fact]
    public async Task SampleAsync_ActionVideo_KeepsTheMaximum()
    {
        RequireFfmpeg();

        var sample = await SampleAsync(SampleMedia.ActionVideo);

        Assert.Empty(sample.Warnings);
        Assert.Equal(16, sample.Frames.Count);
        Assert.Equal(Enumerable.Range(0, 16), sample.Frames.Select(frame => frame.Index));
        Assert.Equal(
            Enumerable.Range(0, 16).Select(slice => TimeSpan.FromSeconds((slice + 0.5) * 5)),
            sample.Frames.Select(frame => frame.Timestamp));
        Assert.All(sample.Frames, frame => Assert.Null(frame.FrameNumber));
    }

    [Fact]
    public async Task SampleAsync_Video_HandsOutOpaqueRgbaWithinTheLongestEdge()
    {
        RequireFfmpeg();

        var sample = await SampleAsync(SampleMedia.ActionVideo, FrameSamplingOptions.Default with { MaxLongestEdge = 64 });

        var frame = sample.Frames[0];
        Assert.Equal((64, 48), (frame.Width, frame.Height));
        Assert.Equal(64 * 48 * 4, frame.Pixels.Length);
        Assert.All(Alphas(frame), alpha => Assert.Equal(255, alpha));
    }

    [Fact]
    public async Task SampleAsync_BlackIntro_DropsTheBlackFrames()
    {
        RequireFfmpeg();

        var sample = await SampleAsync(SampleMedia.BlackIntroVideo);

        Assert.Equal(4, sample.FramesRead);
        Assert.NotEmpty(sample.Frames);
        Assert.All(sample.Frames, frame => Assert.True(frame.Timestamp >= TimeSpan.FromSeconds(1.5)));
    }

    [Fact]
    public async Task SampleAsync_PortraitVideo_IsUpright()
    {
        RequireFfmpeg();

        var sample = await SampleAsync(SampleMedia.PortraitVideo);

        Assert.All(sample.Frames, frame => Assert.Equal((90, 160), (frame.Width, frame.Height)));
    }

    [Fact]
    public async Task SampleAsync_VideoAnalysisUnavailable_GivesAWarning()
    {
        var sample = await SampleAsync(SampleMedia.ShortVideo, videoAnalysis: false);

        Assert.Empty(sample.Frames);
        Assert.Equal(FrameSamplingWarnings.FrameSamplingFfmpegUnavailable, Assert.Single(sample.Warnings).Code);
    }

    [Fact]
    public async Task SampleAsync_GifWithVariableDelays_IsSampledEvenlyInTime()
    {
        // Positions 0.5, 1.5, 2.5 and 3.5 s fall in frames 0, 1, 7 and 12; by index it would be 1, 4, 7 and 10.
        var sample = await SampleAsync(SampleMedia.GifWithVariableDelays);

        Assert.Empty(sample.Warnings);
        Assert.Equal([0, 1, 7, 12], sample.Frames.Select(frame => frame.FrameNumber));
        Assert.Equal(
            [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(3)],
            sample.Frames.Select(frame => frame.Timestamp));
    }

    [Fact]
    public async Task SampleAsync_AnimationShorterThanTheMinimum_UsesAllItsFrames()
    {
        var sample = await SampleAsync(SampleMedia.GifWithThreeFrames);

        Assert.Equal([0, 1, 2], sample.Frames.Select(frame => frame.FrameNumber));
        Assert.Equal(
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400)],
            sample.Frames.Select(frame => frame.Timestamp));
    }

    [Fact]
    public async Task SampleAsync_AnimatedWebP_IsSampled()
    {
        // 6 frames of 120 ms: positions 90, 270, 450 and 630 ms fall in frames 0, 2, 3 and 5.
        var sample = await SampleAsync(SampleMedia.AnimatedWebP);

        Assert.Empty(sample.Warnings);
        Assert.Equal([0, 2, 3, 5], sample.Frames.Select(frame => frame.FrameNumber));
        Assert.All(sample.Frames, frame => Assert.Equal((96, 96), (frame.Width, frame.Height)));
    }

    [Fact]
    public async Task SampleAsync_BlankFirstFrame_IsDropped()
    {
        var sample = await SampleAsync(SampleMedia.GifWithBlankFirstFrame);

        Assert.Equal(4, sample.FramesRead);
        Assert.DoesNotContain(0, sample.Frames.Select(frame => frame.FrameNumber));
    }

    [Fact]
    public async Task SampleAsync_FrameHash_MatchesTheHasherOnItsPixels()
    {
        var sample = await SampleAsync(SampleMedia.AnimatedGif);

        var hasher = new SkiaPerceptualHasher();
        Assert.All(sample.Frames, frame => Assert.Equal(hasher.ComputeFromPixels(frame.Pixels.Span, frame.Width, frame.Height), frame.PerceptualHash));
    }

    [Fact]
    public async Task SampleAsync_UndecodableAnimation_GivesAFailureWarning()
    {
        using var services = CreateServices();
        var content = Content(SampleMedia.CorruptJpeg);
        var format = MediaFormatDetection.Supported(
            services.GetRequiredService<IMediaFormatRegistry>().FindByFormatId("gif")!, MediaType.AnimatedImage, []);

        var sample = await services.GetRequiredService<IFrameSampler>().SampleAsync(content, format, Metadata(content), FrameSamplingOptions.Default, Token);

        Assert.Empty(sample.Frames);
        Assert.Equal(FrameSamplingWarnings.FrameSamplingFailed, Assert.Single(sample.Warnings).Code);
    }

    [Fact]
    public async Task SampleAsync_StillImage_Throws()
    {
        using var services = CreateServices();
        var content = Content(SampleMedia.StaticGif);
        var (format, metadata) = await InspectAsync(services, content, "gif-static.gif");

        await Assert.ThrowsAsync<ArgumentException>(
            () => services.GetRequiredService<IFrameSampler>().SampleAsync(content, format, metadata, FrameSamplingOptions.Default, Token));
    }

    [Fact]
    public async Task SampleAsync_InvalidOptions_Throws()
    {
        using var services = CreateServices();
        var content = Content(SampleMedia.AnimatedGif);
        var (format, metadata) = await InspectAsync(services, content, "gif-animated.gif");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => services.GetRequiredService<IFrameSampler>()
            .SampleAsync(content, format, metadata, FrameSamplingOptions.Default with { MinFrames = 8, MaxFrames = 4 }, Token));
    }

    [Fact]
    public void ToKeyframe_CarriesTheFrameFacts()
    {
        var itemId = Id<MediaItem>.New();
        var frame = new SampledFrame
        {
            Index = 2,
            Timestamp = TimeSpan.FromSeconds(2.5),
            FrameNumber = 7,
            PerceptualHash = 0xABCD,
            Width = 1,
            Height = 1,
            Pixels = new byte[4],
        };

        var keyframe = frame.ToKeyframe(itemId);

        Assert.Equal((itemId, 2, TimeSpan.FromSeconds(2.5), (int?)7, (ulong?)0xABCD), (keyframe.ItemId, keyframe.Index, keyframe.Timestamp, keyframe.FrameNumber, keyframe.PerceptualHash));
    }

    private static async Task<FrameSample> SampleAsync(string path, FrameSamplingOptions? options = null, bool videoAnalysis = true)
    {
        using var services = CreateServices(videoAnalysis);
        var content = Content(path);
        var (format, metadata) = await InspectAsync(services, content, Path.GetFileName(path));
        return await services.GetRequiredService<IFrameSampler>().SampleAsync(content, format, metadata, options ?? FrameSamplingOptions.Default, Token);
    }

    private static TechnicalMetadata Metadata(Abstractions.Metadata.MediaContent content) =>
        new() { ItemId = content.ItemId, ExtractedAt = Modified };

    private static IEnumerable<byte> Alphas(SampledFrame frame)
    {
        var pixels = frame.Pixels.ToArray();
        for (var offset = 3; offset < pixels.Length; offset += 4)
        {
            yield return pixels[offset];
        }
    }
}
