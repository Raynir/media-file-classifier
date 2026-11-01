using System.Buffers.Binary;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Media.Formats;
using MediaFileClassifier.Testing.Media;
using static MediaFileClassifier.Infrastructure.Tests.Media.FormatTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media;

/// <summary>
/// Tests for telling static and animated GIF and WebP files apart (<see cref="GifStructure"/>, <see cref="WebPStructure"/>).
/// </summary>
public sealed class AnimatedImageDetectionTests
{
    [Theory]
    [InlineData("gif-static.gif", MediaType.Image)]
    [InlineData("gif-animated.gif", MediaType.AnimatedImage)]
    [InlineData("webp.webp", MediaType.Image)]
    [InlineData("webp-animated.webp", MediaType.AnimatedImage)]
    public async Task DetectAsync_Samples_AreClassifiedByFrameCount(string sample, MediaType expected)
    {
        var detection = await DetectFileAsync(Path.Combine(SampleMedia.Directory, sample));

        Assert.Equal(expected, detection.MediaType);
    }

    [Fact]
    public async Task CountFramesAsync_AnimatedGif_CountsEveryFrameUpToTheLimit()
    {
        await using var file = File.OpenRead(SampleMedia.AnimatedGif);

        Assert.Equal(8, await GifStructure.CountFramesAsync(file, limit: 100, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CountFramesAsync_AnimatedGif_StopsReadingAtTheLimit()
    {
        await using var file = File.OpenRead(SampleMedia.AnimatedGif);

        Assert.Equal(2, await GifStructure.CountFramesAsync(file, limit: 2, TestContext.Current.CancellationToken));
        Assert.True(file.Position < file.Length / 2, $"Read {file.Position} of {file.Length} bytes.");
    }

    [Fact]
    public async Task CountFramesAsync_TruncatedAnimatedGif_CountsTheFramesBeforeTheEnd()
    {
        var bytes = await File.ReadAllBytesAsync(SampleMedia.AnimatedGif, TestContext.Current.CancellationToken);

        var frames = await GifStructure.CountFramesAsync(new MemoryStream(bytes[..(bytes.Length / 2)]), limit: 100, TestContext.Current.CancellationToken);

        Assert.InRange(frames, 1, 7);
    }

    [Fact]
    public async Task CountFramesAsync_GifHeaderOnly_IsZero()
    {
        Assert.Equal(0, await GifStructure.CountFramesAsync(new MemoryStream("GIF89a"u8.ToArray()), limit: 2, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CountFramesAsync_AnimatedWebP_CountsEveryFrame()
    {
        await using var file = File.OpenRead(SampleMedia.AnimatedWebP);

        Assert.Equal(6, await WebPStructure.CountFramesAsync(file, limit: 100, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DetectAsync_WebPWithAnimationFlagAndOneFrame_IsStill()
    {
        var bytes = WebP(Chunk("VP8X", [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0]), Chunk("ANIM", new byte[6]), Chunk("ANMF", new byte[17]));

        var detection = await DetectBytesAsync(bytes, "one.webp");

        Assert.Equal(MediaType.Image, detection.MediaType);
    }

    [Fact]
    public async Task DetectAsync_ExtendedWebPWithoutAnimationFlag_IsStill()
    {
        var bytes = WebP(Chunk("VP8X", [0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0]), Chunk("ALPH", new byte[3]), Chunk("VP8 ", new byte[10]));

        var detection = await DetectBytesAsync(bytes, "alpha.webp");

        Assert.Equal(MediaType.Image, detection.MediaType);
    }

    [Fact]
    public async Task DetectAsync_WebPWithOddSizedChunks_SkipsThePadding()
    {
        var bytes = WebP(Chunk("VP8X", [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0]), Chunk("ANMF", new byte[17]), Chunk("ANMF", new byte[19]));

        var detection = await DetectBytesAsync(bytes, "two.webp");

        Assert.Equal(MediaType.AnimatedImage, detection.MediaType);
    }

    private static byte[] WebP(params byte[][] chunks)
    {
        var body = chunks.SelectMany(chunk => chunk).ToArray();
        var file = new byte[12 + body.Length];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)(4 + body.Length));
        "WEBP"u8.CopyTo(file.AsSpan(8));
        body.CopyTo(file, 12);
        return file;
    }

    private static byte[] Chunk(string fourCc, byte[] payload)
    {
        var chunk = new byte[8 + payload.Length + (payload.Length & 1)];
        System.Text.Encoding.ASCII.GetBytes(fourCc).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(chunk, 8);
        return chunk;
    }
}
