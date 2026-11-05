using System.Security.Cryptography;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Testing.Media;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;

public sealed class Siglip2ImagePreprocessorTests
{
    public static TheoryData<string> LosslessSamples() =>
        [.. Siglip2TestModel.Reference.Images.Where(image => image.ResizedSha256 is not null).Select(image => image.File)];

    [Theory]
    [MemberData(nameof(LosslessSamples))]
    public void Resize_LosslessSample_MatchesPillowByteForByte(string file)
    {
        var expected = Siglip2TestModel.Reference.Images.Single(image => image.File == file).ResizedSha256;
        var image = new AiImage(File.ReadAllBytes(Path.Combine(SampleMedia.Directory, file)), "image/png", file);

        var resized = Siglip2ImagePreprocessor.Resize(image);

        Assert.Equal(Siglip2Model.ImageSize * Siglip2Model.ImageSize * 3, resized.Length);
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(resized)));
    }

    [Fact]
    public void Decode_ExifOrientation_ReturnsUprightPixels()
    {
        // jpeg-exif.jpg is stored 640 x 480 with orientation 6; the reference (exif_transpose) is 480 x 640.
        var (_, width, height) = Siglip2ImagePreprocessor.Decode(new AiImage(File.ReadAllBytes(SampleMedia.JpegWithExif), "image/jpeg"));

        Assert.Equal((480, 640), (width, height));
    }

    [Fact]
    public void CompositeOverWhite_UsesPillowRounding()
    {
        byte[] pixels = [10, 20, 30, 255, 0, 0, 0, 0, 0, 100, 200, 128];

        Siglip2ImagePreprocessor.CompositeOverWhite(pixels);

        // Opaque unchanged; transparent becomes white; half-transparent blends with Pillow's integer rounding.
        Assert.Equal([10, 20, 30, 255, 255, 255, 255, 255, 127, 177, 227, 255], pixels);
    }

    [Fact]
    public void Prepare_RawRgbaUniformColour_ScalesToMinusOneToOne()
    {
        var pixels = new byte[300 * 200 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (0, 255, 51, 255);
        }

        var values = new float[Siglip2ImagePreprocessor.ValuesPerImage];
        Siglip2ImagePreprocessor.Prepare(AiImage.FromRgba(pixels, 300, 200), values);

        const int Plane = Siglip2Model.ImageSize * Siglip2Model.ImageSize;
        Assert.All(values[..Plane], value => Assert.Equal(-1f, value));
        Assert.All(values[Plane..(2 * Plane)], value => Assert.Equal(1f, value));
        Assert.All(values[(2 * Plane)..], value => Assert.Equal(-0.6f, value, 0.0001f));
    }

    [Fact]
    public void Prepare_Undecodable_ThrowsInvalidData()
    {
        var values = new float[Siglip2ImagePreprocessor.ValuesPerImage];

        Assert.Throws<InvalidDataException>(() => Siglip2ImagePreprocessor.Prepare(new AiImage(new byte[] { 1, 2, 3 }, "image/jpeg"), values));
    }

    [Fact]
    public void FromRgba_WrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => AiImage.FromRgba(new byte[10], 2, 2));
    }

    [Fact]
    public void ResizeToRgb_SameSize_KeepsPixels()
    {
        var rgba = Enumerable.Range(0, 256 * 256 * 4).Select(i => (byte)(i * 7)).ToArray();
        var expected = rgba.Where((_, i) => i % 4 != 3).ToArray();

        Assert.Equal(expected, PilBilinearResampler.ResizeToRgb(rgba, 256, 256, 256, 256));
    }
}
