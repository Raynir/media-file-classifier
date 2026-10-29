using MediaFileClassifier.Testing.Media;

namespace MediaFileClassifier.Infrastructure.Tests.TestSupport;

/// <summary>
/// Checks that the sample media corpus reaches the test output intact.
/// </summary>
public sealed class SampleMediaTests
{
    public static TheoryData<string, string> ValidFiles => new()
    {
        { SampleMedia.JpegWithExif, "FFD8FF" },
        { SampleMedia.PngWithAlpha, "89504E470D0A1A0A" },
        { SampleMedia.StaticGif, "47494638" },
        { SampleMedia.AnimatedGif, "47494638" },
        { SampleMedia.ImageWithText, "89504E470D0A1A0A" },
        { SampleMedia.NonAsciiName, "FFD8FF" },
    };

    [Theory]
    [MemberData(nameof(ValidFiles))]
    public void ValidFile_StartsWithFormatSignature(string path, string signature)
    {
        var header = ReadHeader(path, signature.Length / 2);

        Assert.Equal(signature, Convert.ToHexString(header));
    }

    [Fact]
    public void WebP_HasRiffWebPHeader()
    {
        var header = ReadHeader(SampleMedia.WebP, 12);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(header, 8, 4));
    }

    [Fact]
    public void ShortVideo_HasMp4FileTypeBox()
    {
        var header = ReadHeader(SampleMedia.ShortVideo, 8);

        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(header, 4, 4));
    }

    [Fact]
    public void CorruptJpeg_HasJpegStartButNoEndMarker()
    {
        var bytes = File.ReadAllBytes(SampleMedia.CorruptJpeg);

        Assert.Equal("FFD8FF", Convert.ToHexString(bytes, 0, 3));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(new byte[] { 0xFF, 0xD9 }));
    }

    [Fact]
    public void ZeroByteFile_IsEmpty()
    {
        Assert.Equal(0, new FileInfo(SampleMedia.ZeroByteFile).Length);
    }

    [Fact]
    public void NonAsciiName_IsFoundByEnumeration()
    {
        var names = Directory.EnumerateFiles(SampleMedia.Directory).Select(Path.GetFileName);

        Assert.Contains("фото с пробелом.jpg", names);
    }

    private static byte[] ReadHeader(string path, int length)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[length];
        stream.ReadExactly(header);
        return header;
    }
}
