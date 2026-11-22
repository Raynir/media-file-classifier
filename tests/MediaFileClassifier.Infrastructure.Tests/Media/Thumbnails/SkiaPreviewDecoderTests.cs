using System.Diagnostics;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using MediaFileClassifier.Infrastructure.Tests.TestSupport;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Testing.Timing;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails;

/// <summary>
/// Tests for decoding the details panel preview (E14-S05-T02): screen-sized first, quickly even for a 50-megapixel
/// JPEG, and at full resolution for zooming.
/// </summary>
[Collection(TimingTestsCollection.Name)]
public sealed class SkiaPreviewDecoderTests : IDisposable
{
    private readonly string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"))).FullName;
    private readonly SkiaPreviewDecoder decoder = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task LargeJpeg_DecodesAtScreenSizeQuickly_AndAtFullResolutionOnRequest()
    {
        // 8660 × 5774 ≈ 50 megapixels.
        var path = WriteJpeg(8660, 5774);

        var stopwatch = Stopwatch.StartNew();
        using (var screen = await decoder.DecodeAsync(Content(path), 2560, Token))
        {
            stopwatch.Stop();
            Assert.Equal((2560, 1707, false), (screen.Width, screen.Height, screen.IsFullResolution));
            Assert.True(screen.RowBytes >= screen.Width * 4);
            TestContext.Current.SendDiagnosticMessage($"Screen-sized decode of 50 MP: {stopwatch.ElapsedMilliseconds} ms");
            if (TestPerformance.Enabled)
            {
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"The screen-sized decode took {stopwatch.Elapsed}.");
            }
        }

        using var full = await decoder.DecodeAsync(Content(path), null, Token);
        Assert.Equal((8660, 5774, true), (full.Width, full.Height, full.IsFullResolution));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4096)]
    public async Task SmallImage_IsNeverEnlarged(int? limit)
    {
        using var image = await decoder.DecodeAsync(Content(SampleMedia.PngWithAlpha), limit, Token);

        Assert.True(image.IsFullResolution);
        using var original = SKBitmap.Decode(SampleMedia.PngWithAlpha);
        Assert.Equal((original.Width, original.Height), (image.Width, image.Height));
    }

    [Fact]
    public async Task AnimatedGif_DecodesItsFirstFrame()
    {
        using var image = await decoder.DecodeAsync(Content(SampleMedia.AnimatedGif, localPath: false), 64, Token);

        Assert.True(Math.Max(image.Width, image.Height) <= 64);
    }

    [Fact]
    public async Task CorruptFile_IsReportedAsUndecodable()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => decoder.DecodeAsync(Content(SampleMedia.CorruptJpeg), 256, Token));
    }

    private static MediaContent Content(string path, bool localPath = true) =>
        new(Id<MediaItem>.New(), DateTimeOffset.UnixEpoch, _ => Task.FromResult<Stream>(File.OpenRead(path)))
        {
            LocalPath = localPath ? path : null,
        };

    private string WriteJpeg(int width, int height)
    {
        var path = Path.Combine(folder, "large.jpg");
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height), [SKColors.Teal, SKColors.Orange], SKShaderTileMode.Clamp) })
        {
            canvas.DrawRect(0, 0, width, height, paint);
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }
}
