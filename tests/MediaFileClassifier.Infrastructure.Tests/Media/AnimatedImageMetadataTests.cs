using MediaFileClassifier.Infrastructure.Media.Formats;
using MediaFileClassifier.Testing.Media;
using static MediaFileClassifier.Infrastructure.Tests.Media.ImageMetadataExtractorTests;

namespace MediaFileClassifier.Infrastructure.Tests.Media;

/// <summary>
/// Tests for the frame count, duration and loop count of animated images (<see cref="GifStructure.ReadAsync"/>,
/// <see cref="WebPStructure.ReadAsync"/>). The expected values are the generator's, confirmed with Pillow.
/// </summary>
public sealed class AnimatedImageMetadataTests
{
    [Theory]
    [InlineData("gif-animated.gif", 8, 800)]
    [InlineData("webp-animated.webp", 6, 720)]
    public async Task ExtractAsync_AnimatedSamples_ReadFramesDurationAndLoopCount(string sample, int frames, int milliseconds)
    {
        var metadata = await ExtractFileAsync(Path.Combine(SampleMedia.Directory, sample));

        Assert.Equal((96, 96), (metadata.Width, metadata.Height));
        Assert.Equal(frames, metadata.FrameCount);
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), metadata.Duration);
        Assert.Equal(0, metadata.LoopCount);
        Assert.Empty(metadata.Warnings);
    }

    [Theory]
    [InlineData("gif-static.gif")]
    [InlineData("webp.webp")]
    public async Task ExtractAsync_StillImages_HaveNoAnimationValues(string sample)
    {
        var metadata = await ExtractFileAsync(Path.Combine(SampleMedia.Directory, sample));

        Assert.Null(metadata.FrameCount);
        Assert.Null(metadata.Duration);
        Assert.Null(metadata.LoopCount);
    }

    [Fact]
    public async Task GifReadAsync_DelaysWithoutLoopingExtension_SumsDelaysAndHasNoLoopCount()
    {
        byte[] gif =
        [
            .. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0,
            .. GraphicControl(delayHundredths: 5), .. Frame(),
            .. GraphicControl(delayHundredths: 7), .. Frame(),
            0x3B,
        ];

        var animation = await GifStructure.ReadAsync(new MemoryStream(gif), int.MaxValue, TestContext.Current.CancellationToken);

        Assert.Equal(new AnimationInfo(2, TimeSpan.FromMilliseconds(120), null), animation);
    }

    [Fact]
    public async Task GifReadAsync_LoopCountThree_IsRead()
    {
        byte[] gif =
        [
            .. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0,
            0x21, 0xFF, 11, .. "NETSCAPE2.0"u8, 3, 1, 3, 0, 0,
            .. GraphicControl(delayHundredths: 10), .. Frame(),
            .. GraphicControl(delayHundredths: 10), .. Frame(),
            0x3B,
        ];

        var animation = await GifStructure.ReadAsync(new MemoryStream(gif), int.MaxValue, TestContext.Current.CancellationToken);

        Assert.Equal(new AnimationInfo(2, TimeSpan.FromMilliseconds(200), 3), animation);
    }

    private static byte[] GraphicControl(int delayHundredths) => [0x21, 0xF9, 4, 0, (byte)delayHundredths, (byte)(delayHundredths >> 8), 0, 0];

    /// <summary>
    /// A 1×1 frame: image descriptor, LZW minimum code size and one data sub-block.
    /// </summary>
    private static byte[] Frame() => [0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x4C, 0x01, 0];
}
