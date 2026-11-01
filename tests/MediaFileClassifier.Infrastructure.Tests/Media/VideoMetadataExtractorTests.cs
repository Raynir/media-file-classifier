using System.Text.Json;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Capabilities;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using MediaFileClassifier.Testing.Media;
using Microsoft.Extensions.Logging.Abstractions;
using static MediaFileClassifier.Infrastructure.Tests.Media.FormatTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media;

/// <summary>
/// Tests for <see cref="VideoMetadataExtractor"/>. The expected values were checked with <c>ffprobe</c>. Tests that
/// need FFmpeg skip where it isn't installed, such as on CI; the tests without FFmpeg always run.
/// </summary>
public sealed class VideoMetadataExtractorTests
{
    private static readonly DateTimeOffset Modified = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    [Fact]
    public async Task ExtractAsync_ShortVideo_ReadsStreamsAndContainer()
    {
        var metadata = await ExtractWithFfprobeAsync(SampleMedia.ShortVideo);

        Assert.Equal("mp4", metadata.ContainerFormat);
        Assert.Equal("video/mp4", metadata.MimeType);
        Assert.Equal((320, 240), (metadata.Width, metadata.Height));
        Assert.Equal(2, metadata.Duration!.Value.TotalSeconds, 1);
        Assert.Equal(15, metadata.FrameRate!.Value, 2);
        Assert.Equal("h264", metadata.VideoCodec);
        Assert.Equal("aac", metadata.AudioCodec);
        Assert.True(metadata.HasAudio);
        Assert.True(metadata.BitRate > 0);
        Assert.Empty(metadata.Warnings);
    }

    [Theory]
    [InlineData("video-hevc.mp4", "hevc")]
    [InlineData("video-av1.mp4", "av1")]
    public async Task ExtractAsync_OtherCodecs_ReadCodecAndNoAudio(string sample, string codec)
    {
        var metadata = await ExtractWithFfprobeAsync(Path.Combine(SampleMedia.Directory, sample));

        Assert.Equal((160, 120), (metadata.Width, metadata.Height));
        Assert.Equal(codec, metadata.VideoCodec);
        Assert.Equal(10, metadata.FrameRate!.Value, 2);
        Assert.False(metadata.HasAudio);
        Assert.Null(metadata.AudioCodec);
    }

    [Fact]
    public async Task ExtractAsync_CreationTime_IsTheDateTakenInUtc()
    {
        var metadata = await ExtractWithFfprobeAsync(SampleMedia.PortraitVideo);

        Assert.Equal(new DateTime(2026, 7, 1, 10, 20, 30), metadata.DateTaken);
        Assert.Equal(TimeSpan.Zero, metadata.DateTakenOffset);
        Assert.Equal(DateTakenSource.VideoCreationTime, metadata.DateTakenSource);
    }

    [Fact]
    public async Task ExtractAsync_RawJson_HoldsStreamsAndNoPath()
    {
        var metadata = await ExtractWithFfprobeAsync(SampleMedia.ShortVideo);

        using var json = JsonDocument.Parse(metadata.RawMetadataJson!);
        Assert.Equal(2, json.RootElement.GetProperty("streams").GetArrayLength());
        Assert.Equal("isom", json.RootElement.GetProperty("format").GetProperty("tags").GetProperty("major_brand").GetString());
        Assert.DoesNotContain("video-short", metadata.RawMetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_StreamWithoutPath_IsReadThroughAPipe()
    {
        var metadata = await ExtractWithFfprobeAsync(SampleMedia.ShortVideo, withPath: false);

        Assert.Equal((320, 240), (metadata.Width, metadata.Height));
        Assert.Equal("h264", metadata.VideoCodec);
    }

    [Fact]
    public async Task ExtractAsync_DamagedVideo_KeepsFileLevelMetadataWithWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mfc-tests-{Guid.NewGuid():N}.mp4");
        var bytes = await File.ReadAllBytesAsync(SampleMedia.ShortVideo, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(path, bytes[..200], TestContext.Current.CancellationToken);
        try
        {
            var metadata = await ExtractWithFfprobeAsync(path, detect: false);

            Assert.Equal("mp4", metadata.ContainerFormat);
            Assert.Null(metadata.Width);
            Assert.Equal(MetadataWarnings.VideoProbeFailed, Assert.Single(metadata.Warnings).Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_VideoAnalysisUnavailable_KeepsFileLevelMetadataWithWarning()
    {
        // The probe found no FFmpeg at startup, so the capability is off even if ffprobe turns up later.
        var extractor = CreateExtractor(available: false, FfprobeLocator.GetDefaultSearchDirectories());

        var metadata = await ExtractAsync(extractor, SampleMedia.ShortVideo, withPath: true);

        Assert.Equal("mp4", metadata.ContainerFormat);
        Assert.Equal("video/mp4", metadata.MimeType);
        Assert.Null(metadata.Width);
        Assert.Null(metadata.VideoCodec);
        Assert.Equal([new ExtractionWarning(MetadataWarnings.FfmpegUnavailable)], metadata.Warnings);
    }

    [Fact]
    public async Task ExtractAsync_FfprobeMissing_KeepsFileLevelMetadataWithWarning()
    {
        var extractor = CreateExtractor(available: true, [Path.Combine(Path.GetTempPath(), "mfc-no-ffmpeg-here")]);

        var metadata = await ExtractAsync(extractor, SampleMedia.ShortVideo, withPath: true);

        Assert.Equal(MetadataWarnings.FfmpegUnavailable, Assert.Single(metadata.Warnings).Code);
    }

    [Fact]
    public void Mp4Handler_UsesTheVideoExtractor()
    {
        Assert.IsType<VideoMetadataExtractor>(CreateRegistry().FindByFormatId("mp4")?.MetadataExtractor);
    }

    /// <summary>
    /// Reads a video with the installed FFmpeg, or skips the test without it.
    /// </summary>
    internal static async Task<TechnicalMetadata> ExtractWithFfprobeAsync(string path, bool withPath = true, bool detect = true, DateTimeOffset? modified = null)
    {
        if (new FfprobeLocator(FfprobeLocator.GetDefaultSearchDirectories()).FindFolder() is null)
        {
            Assert.Skip("FFmpeg isn't installed on this machine.");
        }

        return await ExtractAsync(CreateExtractor(available: true, FfprobeLocator.GetDefaultSearchDirectories()), path, withPath, detect, modified);
    }

    private static VideoMetadataExtractor CreateExtractor(bool available, IReadOnlyList<string> searchDirectories)
    {
        var capabilities = new CapabilityService([], NullLogger<CapabilityService>.Instance);
        if (available)
        {
            capabilities.SetAvailable(KnownCapabilities.VideoAnalysis);
        }

        return new VideoMetadataExtractor(capabilities, new FfprobeLocator(searchDirectories), TimeProvider.System, NullLogger<VideoMetadataExtractor>.Instance);
    }

    private static async Task<TechnicalMetadata> ExtractAsync(VideoMetadataExtractor extractor, string path, bool withPath, bool detect = true, DateTimeOffset? modified = null)
    {
        // Without detection, the file is taken as a supported MP4, so damage the detector would catch reaches FFprobe.
        var detection = detect
            ? await DetectFileAsync(path)
            : MediaFormatDetection.Supported(CreateRegistry().FindByFormatId("mp4")!, MediaType.Video, []);
        var content = new MediaContent(Id<MediaItem>.New(), modified ?? Modified, _ => Task.FromResult<Stream>(File.OpenRead(path)))
        {
            LocalPath = withPath ? path : null,
        };
        return await extractor.ExtractAsync(content, detection, TestContext.Current.CancellationToken);
    }
}
