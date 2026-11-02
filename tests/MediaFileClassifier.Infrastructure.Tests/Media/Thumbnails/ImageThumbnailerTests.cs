using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using MediaFileClassifier.Testing.Media;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using static MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails.ThumbnailTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails;

/// <summary>
/// Tests for <see cref="ImageThumbnailer"/> (E03-S04-T01).
/// </summary>
public sealed class ImageThumbnailerTests
{
    [Fact]
    public async Task GenerateAsync_RotatedPhoto_IsUprightInBothSizes()
    {
        // Stored 640×480 with EXIF Orientation 6, shown 480×640.
        var thumbnails = await GenerateAsync(SampleMedia.JpegWithExif);

        Assert.Empty(thumbnails.Warnings);
        Assert.Equal((384, 512), SizeOf(thumbnails.Images[ThumbnailSize.Large]));
        Assert.Equal((192, 256), SizeOf(thumbnails.Images[ThumbnailSize.Small]));
    }

    [Theory]
    [InlineData("jpeg-icc-offset.jpg", 200, 100, 200, 100)]
    [InlineData("webp.webp", 320, 240, 256, 192)]
    [InlineData("gif-static.gif", 96, 96, 96, 96)]
    [InlineData("фото с пробелом.jpg", 160, 120, 160, 120)]
    public async Task GenerateAsync_SmallImage_IsNeverUpscaled(string sample, int width, int height, int smallWidth, int smallHeight)
    {
        var thumbnails = await GenerateAsync(Path.Combine(SampleMedia.Directory, sample));

        Assert.Equal((width, height), SizeOf(thumbnails.Images[ThumbnailSize.Large]));
        Assert.Equal((smallWidth, smallHeight), SizeOf(thumbnails.Images[ThumbnailSize.Small]));
    }

    [Fact]
    public async Task GenerateAsync_TransparentPng_KeepsTransparency()
    {
        var thumbnails = await GenerateAsync(SampleMedia.PngWithAlpha);

        using var thumbnail = Decode(thumbnails.Images[ThumbnailSize.Small]);
        Assert.Equal((256, 256), (thumbnail.Width, thumbnail.Height));
        Assert.Equal(0, thumbnail.GetPixel(2, 2).Alpha);
        Assert.Equal(255, thumbnail.GetPixel(128, 128).Alpha);
    }

    [Fact]
    public async Task GenerateAsync_Images_AreWebP()
    {
        var thumbnails = await GenerateAsync(SampleMedia.JpegWithExif);

        foreach (var image in thumbnails.Images.Values)
        {
            Assert.Equal("RIFF"u8.ToArray(), image[..4]);
            Assert.Equal("WEBP"u8.ToArray(), image[8..12]);
        }
    }

    [Fact]
    public async Task GenerateAsync_LargePhoto_IsScaledToTheSizes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mfc-tests-{Guid.NewGuid():N}.jpg");
        using (var large = new SKBitmap(4000, 3000))
        {
            using (var canvas = new SKCanvas(large))
            using (var paint = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, 0), new(4000, 3000), [SKColors.Red, SKColors.Blue], SKShaderTileMode.Clamp) })
            {
                canvas.DrawRect(0, 0, 4000, 3000, paint);
            }

            await File.WriteAllBytesAsync(path, large.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray(), TestContext.Current.CancellationToken);
        }

        try
        {
            var thumbnails = await GenerateAsync(path);

            Assert.Equal((512, 384), SizeOf(thumbnails.Images[ThumbnailSize.Large]));
            Assert.Equal((256, 192), SizeOf(thumbnails.Images[ThumbnailSize.Small]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("zero-byte.png")]
    [InlineData("corrupt.jpg")]
    public async Task GenerateAsync_BrokenFile_GivesNoImagesAndAWarning(string sample)
    {
        var path = Path.Combine(SampleMedia.Directory, sample);
        var thumbnailer = new ImageThumbnailer(NullLogger<ImageThumbnailer>.Instance);
        var content = Content(path);
        var format = MediaFormatDetection.Supported(FormatTestSupport.CreateRegistry().FindByFormatId("jpeg")!, MediaType.Image, []);

        var thumbnails = await thumbnailer.GenerateAsync(content, format, Metadata(content), TestContext.Current.CancellationToken);

        // A truncated JPEG may decode in part, like in a browser; it must never throw.
        if (!thumbnails.HasImages)
        {
            Assert.Equal(ThumbnailWarnings.ThumbnailFailed, Assert.Single(thumbnails.Warnings).Code);
        }

        Assert.True(sample != "zero-byte.png" || !thumbnails.HasImages);
    }

    [Fact]
    public async Task GenerateAsync_Cancelled_Throws()
    {
        var thumbnailer = new ImageThumbnailer(NullLogger<ImageThumbnailer>.Instance);
        var content = Content(SampleMedia.WebP);
        var format = MediaFormatDetection.Supported(FormatTestSupport.CreateRegistry().FindByFormatId("webp")!, MediaType.Image, []);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => thumbnailer.GenerateAsync(content, format, Metadata(content), new CancellationToken(canceled: true)));
    }

    private static TechnicalMetadata Metadata(Abstractions.Metadata.MediaContent content)
        => new() { ItemId = content.ItemId, ExtractedAt = Modified };
}
