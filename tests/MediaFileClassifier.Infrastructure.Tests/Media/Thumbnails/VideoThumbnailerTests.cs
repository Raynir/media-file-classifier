using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using MediaFileClassifier.Testing.Media;
using Microsoft.Extensions.DependencyInjection;
using static MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails.ThumbnailTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails;

/// <summary>
/// Tests for <see cref="VideoThumbnailer"/> (E03-S04-T03). Tests that need FFmpeg skip where it isn't installed, such
/// as on CI; the tests without FFmpeg always run.
/// </summary>
public sealed class VideoThumbnailerTests
{
    [Theory]
    [InlineData("video-short.mp4", 320, 240)]
    [InlineData("video-hevc.mp4", 160, 120)]
    [InlineData("video-av1.mp4", 160, 120)]
    public async Task GenerateAsync_Video_GetsAFrameThumbnail(string sample, int width, int height)
    {
        RequireFfmpeg();

        var thumbnails = await GenerateAsync(Path.Combine(SampleMedia.Directory, sample));

        Assert.Empty(thumbnails.Warnings);
        Assert.Equal((width, height), SizeOf(thumbnails.Images[ThumbnailSize.Large]));
        using var frame = Decode(thumbnails.Images[ThumbnailSize.Small]);
        Assert.False(FrameStatistics.Measure(frame).IsBlankVideoFrame);
    }

    [Fact]
    public async Task GenerateAsync_BlackIntro_SkipsTheBlackFrames()
    {
        RequireFfmpeg();

        var thumbnails = await GenerateAsync(SampleMedia.BlackIntroVideo);

        using var frame = Decode(thumbnails.Images[ThumbnailSize.Large]);
        Assert.False(FrameStatistics.Measure(frame).IsBlankVideoFrame);
    }

    [Fact]
    public async Task GenerateAsync_PortraitVideo_IsUpright()
    {
        // Stored 160×90 with a 90° display rotation; FFmpeg turns the frame upright itself.
        RequireFfmpeg();

        var thumbnails = await GenerateAsync(SampleMedia.PortraitVideo);

        Assert.Equal((90, 160), SizeOf(thumbnails.Images[ThumbnailSize.Large]));
    }

    [Fact]
    public async Task GenerateAsync_StreamWithoutPath_IsCopiedForFfmpeg()
    {
        RequireFfmpeg();

        var thumbnails = await GenerateAsync(SampleMedia.ShortVideo, withPath: false);

        Assert.True(thumbnails.HasImages);
    }

    [Fact]
    public async Task GenerateAsync_VideoAnalysisUnavailable_GivesAPlaceholderWarning()
    {
        var thumbnails = await GenerateAsync(SampleMedia.ShortVideo, videoAnalysis: false);

        Assert.False(thumbnails.HasImages);
        Assert.Equal(ThumbnailWarnings.ThumbnailFfmpegUnavailable, Assert.Single(thumbnails.Warnings).Code);
    }

    [Fact]
    public async Task GenerateAsync_DamagedVideo_GivesAFailureWarning()
    {
        RequireFfmpeg();
        var path = Path.Combine(Path.GetTempPath(), $"mfc-tests-{Guid.NewGuid():N}.mp4");
        var bytes = await File.ReadAllBytesAsync(SampleMedia.ShortVideo, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(path, bytes[..2000], TestContext.Current.CancellationToken);
        try
        {
            using var services = CreateServices();
            var content = Content(path);
            var format = Abstractions.Formats.MediaFormatDetection.Supported(
                services.GetRequiredService<Abstractions.Formats.IMediaFormatRegistry>().FindByFormatId("mp4")!, Domain.Media.MediaType.Video, []);
            var metadata = new Domain.Metadata.TechnicalMetadata { ItemId = content.ItemId, ExtractedAt = Modified, Duration = TimeSpan.FromSeconds(2) };

            var thumbnails = await format.Handler!.ThumbnailGenerator.GenerateAsync(content, format, metadata, TestContext.Current.CancellationToken);

            Assert.False(thumbnails.HasImages);
            Assert.Equal(ThumbnailWarnings.ThumbnailFailed, Assert.Single(thumbnails.Warnings).Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPositions_KnownDuration_TriesTenThenTwentyFiveThenFiftyPercentThenTheStart()
    {
        Assert.Equal(
            [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(50), TimeSpan.Zero],
            VideoThumbnailer.GetPositions(TimeSpan.FromSeconds(100)));
    }

    [Fact]
    public void GetPositions_UnknownDuration_TriesTheStart()
    {
        Assert.Equal([TimeSpan.Zero], VideoThumbnailer.GetPositions(null));
    }
}
