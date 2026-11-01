using System.Text.Json;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using MediaFileClassifier.Testing.Media;
using Microsoft.Extensions.DependencyInjection;
using static MediaFileClassifier.Infrastructure.Tests.Media.FormatTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media;

/// <summary>
/// Tests for <see cref="ImageMetadataExtractor"/>. The expected values were checked against ImageMagick
/// (<c>magick identify -verbose</c>) and the generator script.
/// </summary>
public sealed class ImageMetadataExtractorTests
{
    [Fact]
    public async Task ExtractAsync_JpegWithExif_ReadsCameraDateOrientationAndGps()
    {
        var metadata = await ExtractFileAsync(SampleMedia.JpegWithExif);

        Assert.Equal("jpeg", metadata.ContainerFormat);
        Assert.Equal("image/jpeg", metadata.MimeType);
        Assert.Equal((640, 480), (metadata.Width, metadata.Height));
        Assert.Equal(ImageOrientation.Rotate90, metadata.Orientation);
        Assert.Equal("MFC Test Camera Co.", metadata.CameraMake);
        Assert.Equal("Sample 1", metadata.CameraModel);
        Assert.Null(metadata.LensModel);
        Assert.Equal(new DateTime(2026, 10, 29, 12, 34, 56), metadata.DateTaken);
        Assert.Null(metadata.DateTakenOffset);
        Assert.Equal(DateTakenSource.ExifDateTimeOriginal, metadata.DateTakenSource);

        // 55°58'48" S, 67°17'21" W.
        Assert.NotNull(metadata.Location);
        Assert.Equal(-(55 + (58 / 60.0) + (48 / 3600.0)), metadata.Location.Latitude, 6);
        Assert.Equal(-(67 + (17 / 60.0) + (21 / 3600.0)), metadata.Location.Longitude, 6);
        Assert.Empty(metadata.Warnings);
    }

    [Fact]
    public async Task ExtractAsync_JpegWithIccAndOffset_ReadsColorSpaceLensOffsetAndSubseconds()
    {
        var metadata = await ExtractFileAsync(SampleMedia.JpegWithIccAndOffset);

        Assert.Equal((200, 100), (metadata.Width, metadata.Height));
        Assert.Equal(ImageOrientation.Normal, metadata.Orientation);
        Assert.Equal("sRGB built-in", metadata.ColorSpace);
        Assert.Equal("MFC Lens 35mm", metadata.LensModel);
        Assert.Equal(new DateTime(2026, 6, 15, 8, 30, 0, 250), metadata.DateTaken);
        Assert.Equal(TimeSpan.FromHours(3), metadata.DateTakenOffset);
        Assert.Null(metadata.Location);
    }

    [Theory]
    [InlineData("png-alpha.png", "png", 256, 256)]
    [InlineData("ocr-text.png", "png", 640, 200)]
    [InlineData("webp.webp", "webp", 320, 240)]
    [InlineData("gif-static.gif", "gif", 96, 96)]
    [InlineData("фото с пробелом.jpg", "jpeg", 160, 120)]
    public async Task ExtractAsync_Samples_ReadDimensions(string sample, string formatId, int width, int height)
    {
        var metadata = await ExtractFileAsync(Path.Combine(SampleMedia.Directory, sample));

        Assert.Equal(formatId, metadata.ContainerFormat);
        Assert.Equal((width, height), (metadata.Width, metadata.Height));
        Assert.Equal(ImageOrientation.Normal, metadata.Orientation);
        Assert.Null(metadata.CameraMake);
        Assert.Empty(metadata.Warnings);
    }

    [Fact]
    public async Task ExtractAsync_RawJson_HoldsEveryDirectoryAndNoPath()
    {
        var metadata = await ExtractFileAsync(SampleMedia.JpegWithExif);

        using var json = JsonDocument.Parse(metadata.RawMetadataJson!);
        Assert.Equal("MFC Test Camera Co.", json.RootElement.GetProperty("Exif IFD0").GetProperty("Make").GetString());
        Assert.True(json.RootElement.TryGetProperty("GPS", out _));
        Assert.DoesNotContain(SampleMedia.Directory, metadata.RawMetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_CorruptJpeg_ReturnsFileLevelMetadataWithWarning()
    {
        // The reader gives up on the junk after the frame header, so nothing but the format and the dates is known.
        var metadata = await ExtractFileAsync(SampleMedia.CorruptJpeg);

        Assert.Equal("jpeg", metadata.ContainerFormat);
        Assert.Equal("image/jpeg", metadata.MimeType);
        Assert.Equal(MetadataWarnings.MetadataUnreadable, Assert.Single(metadata.Warnings).Code);
    }

    [Fact]
    public async Task ExtractAsync_JunkAfterSignature_ReturnsFileLevelMetadataWithWarning()
    {
        byte[] junk = [0xFF, 0xD8, 0xFF, .. new byte[40]];

        var metadata = await ExtractBytesAsync(junk, "jpeg");

        Assert.Equal("jpeg", metadata.ContainerFormat);
        Assert.Null(metadata.Width);
        Assert.Contains(metadata.Warnings, warning => warning.Code.StartsWith("Metadata", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractAsync_StreamThatCannotSeek_ReadsTheSameValues()
    {
        var item = Id<MediaItem>.New();
        var content = new MediaContent(item, DateTimeOffset.UnixEpoch, _ => Task.FromResult<Stream>(new NonSeekableStream(File.OpenRead(SampleMedia.JpegWithExif))));

        var metadata = await ImageExtractor().ExtractAsync(content, await DetectFileAsync(SampleMedia.JpegWithExif), TestContext.Current.CancellationToken);

        Assert.Equal(item, metadata.ItemId);
        Assert.Equal("Sample 1", metadata.CameraModel);
    }

    [Fact]
    public async Task ExtractAsync_UnsupportedFormat_Throws()
    {
        var content = new MediaContent(Id<MediaItem>.New(), DateTimeOffset.UnixEpoch, _ => Task.FromResult<Stream>(new MemoryStream()));
        var rejected = MediaFormatDetection.Rejected(new(MediaFormatIssues.EmptyFile));

        await Assert.ThrowsAsync<ArgumentException>(() => ImageExtractor().ExtractAsync(content, rejected, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Handlers_UseTheImageExtractorForImagesOnly()
    {
        var registry = CreateRegistry();

        Assert.All(
            ["jpeg", "png", "webp", "gif"],
            format => Assert.IsType<ImageMetadataExtractor>(registry.FindByFormatId(format)?.MetadataExtractor));
    }

    /// <summary>
    /// Detects a sample and reads its metadata through its handler, as the import does.
    /// </summary>
    internal static async Task<TechnicalMetadata> ExtractFileAsync(string path, DateTimeOffset? lastModified = null)
    {
        var detection = await DetectFileAsync(path);
        Assert.True(detection.IsSupported, detection.Rejection?.ToString());
        var content = new MediaContent(Id<MediaItem>.New(), lastModified ?? File.GetLastWriteTimeUtc(path), _ => Task.FromResult<Stream>(File.OpenRead(path)))
        {
            LocalPath = path,
        };
        return await detection.Handler!.MetadataExtractor.ExtractAsync(content, detection, TestContext.Current.CancellationToken);
    }

    private static async Task<TechnicalMetadata> ExtractBytesAsync(byte[] bytes, string formatId)
    {
        await using var services = CreateServices();
        var handler = services.GetRequiredService<IMediaFormatRegistry>().FindByFormatId(formatId)!;
        var content = new MediaContent(Id<MediaItem>.New(), DateTimeOffset.UnixEpoch, _ => Task.FromResult<Stream>(new MemoryStream(bytes)));
        return await handler.MetadataExtractor.ExtractAsync(content, MediaFormatDetection.Supported(handler, MediaType.Image, []), TestContext.Current.CancellationToken);
    }
}
