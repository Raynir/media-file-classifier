using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Testing.Media;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Tests.Hashing;

/// <summary>
/// Tests for <see cref="SkiaPerceptualHasher"/>. These tests make their copies with SkiaSharp; the committed
/// robustness test set (E01-S06-T06) is checked separately.
/// </summary>
public sealed class SkiaPerceptualHasherTests
{
    private readonly SkiaPerceptualHasher hasher = new();

    public static TheoryData<string> DecodableImages =>
    [
        SampleMedia.JpegWithExif,
        SampleMedia.PngWithAlpha,
        SampleMedia.WebP,
        SampleMedia.StaticGif,
        SampleMedia.AnimatedGif,
        SampleMedia.ImageWithText,
        SampleMedia.NonAsciiName,
    ];

    public static TheoryData<string> NotImages => [SampleMedia.CorruptJpeg, SampleMedia.ZeroByteFile, SampleMedia.ShortVideo];

    [Theory]
    [MemberData(nameof(DecodableImages))]
    public void Compute_Image_GivesAHash(string path)
    {
        Assert.NotNull(Hash(path));
    }

    [Theory]
    [MemberData(nameof(NotImages))]
    public void Compute_NotADecodableImage_GivesNull(string path)
    {
        Assert.Null(Hash(path));
    }

    [Fact]
    public void Compute_NonSeekableStream_GivesSameHash()
    {
        using var stream = new NonSeekableStream(File.OpenRead(SampleMedia.JpegWithExif));

        Assert.Equal(Hash(SampleMedia.JpegWithExif), hasher.Compute(stream));
    }

    [Fact]
    public void Compute_AnimatedImage_IsHashedByItsFirstFrame()
    {
        // The static sample is the animated sample's first frame.
        Assert.Equal(Hash(SampleMedia.StaticGif), Hash(SampleMedia.AnimatedGif));
    }

    [Theory]
    [InlineData(1.0, SKEncodedImageFormat.Jpeg, 30)]
    [InlineData(1.0, SKEncodedImageFormat.Jpeg, 10)]
    [InlineData(0.5, SKEncodedImageFormat.Jpeg, 80)]
    [InlineData(0.25, SKEncodedImageFormat.Jpeg, 80)]
    [InlineData(1.5, SKEncodedImageFormat.Png, 100)]
    [InlineData(0.5, SKEncodedImageFormat.Webp, 60)]
    public void Compute_ResizedOrRecompressedCopy_IsWithinThreshold(double scale, SKEncodedImageFormat format, int quality)
    {
        var original = Hash(SampleMedia.ImageWithText)!.Value;

        var copy = hasher.Compute(new MemoryStream(Reencode(SampleMedia.ImageWithText, scale, format, quality)))!.Value;

        Assert.InRange(PerceptualHash.Distance(original, copy), 0, PerceptualHash.SamePictureThreshold);
    }

    [Fact]
    public void Compute_UnrelatedImages_AreOutsideThreshold()
    {
        string[] unrelated = [SampleMedia.JpegWithExif, SampleMedia.StaticGif, SampleMedia.ImageWithText];
        var hashes = unrelated.Select(path => Hash(path)!.Value).ToList();

        for (var i = 0; i < hashes.Count; i++)
        {
            for (var j = i + 1; j < hashes.Count; j++)
            {
                Assert.True(
                    PerceptualHash.Distance(hashes[i], hashes[j]) > PerceptualHash.SamePictureThreshold,
                    $"{unrelated[i]} and {unrelated[j]} are {PerceptualHash.Distance(hashes[i], hashes[j])} bits apart.");
            }
        }
    }

    [Fact]
    public void Distance_CountsDifferentBits()
    {
        Assert.Equal(0, PerceptualHash.Distance(0xF0F0, 0xF0F0));
        Assert.Equal(64, PerceptualHash.Distance(0, ulong.MaxValue));
        Assert.Equal(2, PerceptualHash.Distance(0b1010, 0b0110));
    }

    [Fact]
    public void ComputeFromPixels_DecodedPng_GivesTheSameHashAsTheFile()
    {
        // PNGs decode at full size, so both paths see the same pixels.
        using var codec = SKCodec.Create(SampleMedia.PngWithAlpha);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, bitmap.GetPixels()));

        var fromPixels = hasher.ComputeFromPixels(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height);

        Assert.Equal(Hash(SampleMedia.PngWithAlpha), fromPixels);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(2, 2, 15)]
    public void ComputeFromPixels_BufferOfWrongSize_Throws(int width, int height, int bytes)
    {
        Assert.ThrowsAny<ArgumentException>(() => hasher.ComputeFromPixels(new byte[bytes], width, height));
    }

    private static byte[] Reencode(string path, double scale, SKEncodedImageFormat format, int quality)
    {
        using var bitmap = SKBitmap.Decode(path);
        var info = new SKImageInfo((int)(bitmap.Width * scale), (int)(bitmap.Height * scale));
        using var resized = bitmap.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    private ulong? Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return hasher.Compute(stream);
    }
}
