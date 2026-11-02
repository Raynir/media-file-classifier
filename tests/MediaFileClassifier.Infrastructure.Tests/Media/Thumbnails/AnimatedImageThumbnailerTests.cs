using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using MediaFileClassifier.Testing.Media;
using SkiaSharp;
using static MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails.ThumbnailTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails;

/// <summary>
/// Tests for <see cref="AnimatedImageThumbnailer"/> (E03-S04-T02).
/// </summary>
public sealed class AnimatedImageThumbnailerTests
{
    [Fact]
    public async Task GenerateAsync_GifWithBlankFirstFrame_ShowsALaterFrame()
    {
        var thumbnails = await GenerateAsync(SampleMedia.GifWithBlankFirstFrame);

        using var thumbnail = Decode(thumbnails.Images[ThumbnailSize.Large]);
        Assert.False(FrameStatistics.Measure(thumbnail).IsUniform);
        Assert.True(IsRed(thumbnail.GetPixel(16, 16)), $"Expected the red square of the second frame, got {thumbnail.GetPixel(16, 16)}.");
    }

    [Theory]
    [InlineData("gif-animated.gif")]
    [InlineData("webp-animated.webp")]
    public async Task GenerateAsync_Animation_ShowsTheFirstFrame(string sample)
    {
        // The first frame has content, so it is the one shown: the red square in the top left corner.
        var thumbnails = await GenerateAsync(Path.Combine(SampleMedia.Directory, sample));

        using var thumbnail = Decode(thumbnails.Images[ThumbnailSize.Small]);
        Assert.Equal((96, 96), (thumbnail.Width, thumbnail.Height));
        Assert.True(IsRed(thumbnail.GetPixel(16, 16)));
        Assert.False(IsRed(thumbnail.GetPixel(80, 80)));
    }

    [Fact]
    public void DecodeRepresentativeFrame_AllFramesBlank_ShowsTheFirst()
    {
        using var data = SKData.CreateCopy(BlankAnimatedGif());
        using var codec = SKCodec.Create(data);

        using var frame = AnimatedImageThumbnailer.DecodeRepresentativeFrame(codec, TestContext.Current.CancellationToken);

        Assert.Equal(SKColors.White, frame.GetPixel(5, 5));
    }

    private static bool IsRed(SKColor color) => color.Red > 150 && color.Green < 100 && color.Blue < 100;

    /// <summary>
    /// Builds a two-frame animated GIF whose frames are plain white and plain black.
    /// </summary>
    private static byte[] BlankAnimatedGif()
    {
        // GIF89a, 10×10, global colour table of two colours: white and black.
        var bytes = new List<byte>();
        bytes.AddRange("GIF89a"u8.ToArray());
        bytes.AddRange([10, 0, 10, 0, 0x80, 0, 0]);
        bytes.AddRange([255, 255, 255, 0, 0, 0]);
        bytes.AddRange([0x21, 0xFF, 11, .. "NETSCAPE2.0"u8.ToArray(), 3, 1, 0, 0, 0]);
        foreach (var colorIndex in new byte[] { 0, 1 })
        {
            bytes.AddRange([0x21, 0xF9, 4, 0, 10, 0, 0, 0]);
            bytes.AddRange([0x2C, 0, 0, 0, 0, 10, 0, 10, 0, 0]);
            bytes.AddRange(LzwSolid(colorIndex, 100));
        }

        bytes.Add(0x3B);
        return [.. bytes];
    }

    /// <summary>
    /// LZW-codes a run of one colour with a minimum code size of 2, emitting a clear code before every pixel so the
    /// code width stays at 3 bits.
    /// </summary>
    private static byte[] LzwSolid(byte colorIndex, int pixels)
    {
        const int Clear = 4;
        const int End = 5;
        var codes = new List<int>();
        for (var i = 0; i < pixels; i++)
        {
            codes.Add(Clear);
            codes.Add(colorIndex);
        }

        codes.Add(End);
        var packed = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var code in codes)
        {
            buffer |= code << bits;
            bits += 3;
            while (bits >= 8)
            {
                packed.Add((byte)(buffer & 0xFF));
                buffer >>= 8;
                bits -= 8;
            }
        }

        if (bits > 0)
        {
            packed.Add((byte)buffer);
        }

        var result = new List<byte> { 2 };
        for (var offset = 0; offset < packed.Count; offset += 255)
        {
            var length = Math.Min(255, packed.Count - offset);
            result.Add((byte)length);
            result.AddRange(packed.GetRange(offset, length));
        }

        result.Add(0);
        return [.. result];
    }
}
