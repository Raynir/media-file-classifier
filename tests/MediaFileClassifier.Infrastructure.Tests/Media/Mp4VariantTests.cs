using System.Buffers.Binary;
using System.Text;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Media.Formats;
using MediaFileClassifier.Testing.Media;
using static MediaFileClassifier.Infrastructure.Tests.Media.FormatTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media;

/// <summary>
/// Tests for the MP4 variants (<see cref="Mp4FormatHandler"/>, <see cref="Mp4Structure"/>): any video track is a video,
/// audio-only files and the related containers are skipped.
/// </summary>
public sealed class Mp4VariantTests
{
    [Theory]
    [InlineData("video-short.mp4", "avc1")]
    [InlineData("video-hevc.mp4", "hvc1")]
    [InlineData("video-av1.mp4", "av01")]
    public async Task Samples_WithVideoTrack_AreVideos(string sample, string codec)
    {
        var path = Path.Combine(SampleMedia.Directory, sample);
        var detection = await DetectFileAsync(path);
        await using var file = File.OpenRead(path);
        var summary = await Mp4Structure.ReadAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal(MediaType.Video, detection.MediaType);
        Assert.Contains(new Mp4Track("vide", codec), summary.Tracks!);
    }

    [Fact]
    public async Task Sample_AudioOnly_IsRejected()
    {
        var detection = await DetectFileAsync(SampleMedia.AudioOnlyMp4);

        Assert.False(detection.IsSupported);
        Assert.Equal("mp4", detection.Handler?.FormatId);
        Assert.Equal(MediaFormatIssues.NoVideoTrack, detection.Rejection?.Code);
    }

    [Fact]
    public async Task Sample_ShortVideo_HasVideoAndAudioTracks()
    {
        await using var file = File.OpenRead(SampleMedia.ShortVideo);

        var summary = await Mp4Structure.ReadAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal("isom", summary.MajorBrand);
        Assert.Equal([new Mp4Track("vide", "avc1"), new Mp4Track("soun", "mp4a")], summary.Tracks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DetectAsync_MovieBoxAfterMediaData_IsFound(bool seekable)
    {
        // Files written without "faststart" keep the moov box at the end.
        var bytes = Concat(FileType("mp42"), Box("mdat", new byte[100_000]), Movie(Track("vide", "hev1")));
        Stream content = seekable ? new MemoryStream(bytes) : new NonSeekableStream(new MemoryStream(bytes));

        var detection = await CreateRegistry().DetectAsync(content, "clip.mp4", TestContext.Current.CancellationToken);

        Assert.Equal(MediaType.Video, detection.MediaType);
    }

    [Fact]
    public async Task DetectAsync_LargeSizeMediaData_IsSkipped()
    {
        var mediaData = new byte[16 + 50];
        BinaryPrimitives.WriteUInt32BigEndian(mediaData, 1);
        "mdat"u8.CopyTo(mediaData.AsSpan(4));
        BinaryPrimitives.WriteUInt64BigEndian(mediaData.AsSpan(8), (ulong)mediaData.Length);
        var bytes = Concat(FileType("isom"), mediaData, Movie(Track("vide", "avc1")));

        var detection = await DetectBytesAsync(bytes, "clip.mp4");

        Assert.Equal(MediaType.Video, detection.MediaType);
    }

    [Fact]
    public async Task DetectAsync_UncommonVideoCodec_IsStillAVideo()
    {
        var bytes = Concat(FileType("isom"), Movie(Track("soun", "mp4a"), Track("vide", "mp4v")));

        var detection = await DetectBytesAsync(bytes, "old.mp4");

        Assert.Equal(MediaType.Video, detection.MediaType);
    }

    [Theory]
    [InlineData("qt  ", "qt")]
    [InlineData("3gp4", "3gp4")]
    [InlineData("heic", "heic")]
    [InlineData("avif", "avif")]
    public async Task DetectAsync_RelatedContainer_IsRejected(string brand, string detail)
    {
        var bytes = Concat(FileType(brand), Movie(Track("vide", "avc1")));

        var detection = await DetectBytesAsync(bytes, "clip.mp4");

        Assert.Equal(new MediaFormatIssue(MediaFormatIssues.UnsupportedContainer, detail), detection.Rejection);
    }

    [Fact]
    public async Task DetectAsync_NoMovieBox_IsRejectedAsDamaged()
    {
        // An interrupted download: the media data was written, the moov box never was.
        var bytes = Concat(FileType("isom"), Box("mdat", new byte[64]));

        var detection = await DetectBytesAsync(bytes, "partial.mp4");

        Assert.Equal(new MediaFormatIssue(MediaFormatIssues.DamagedFile, "no moov box"), detection.Rejection);
    }

    [Fact]
    public async Task DetectAsync_TruncatedSample_IsRejectedAsDamaged()
    {
        var bytes = await File.ReadAllBytesAsync(SampleMedia.ShortVideo, TestContext.Current.CancellationToken);

        // faststart puts the moov box near the start; cut inside it.
        var detection = await DetectBytesAsync(bytes[..200], "cut.mp4");

        Assert.Equal(MediaFormatIssues.DamagedFile, detection.Rejection?.Code);
    }

    [Fact]
    public async Task DetectAsync_BrokenTrackBoxes_HasNoVideoTrackAndDoesNotThrow()
    {
        var brokenTrack = Box("trak", [0, 0, 0, 200, .. "mdia"u8, 1, 2, 3]);
        var bytes = Concat(FileType("isom"), Movie(brokenTrack));

        var detection = await DetectBytesAsync(bytes, "broken.mp4");

        Assert.Equal(MediaFormatIssues.NoVideoTrack, detection.Rejection?.Code);
    }

    private static byte[] FileType(string majorBrand)
        => Box("ftyp", [.. Encoding.ASCII.GetBytes(majorBrand), 0, 0, 2, 0, .. "isommp41"u8]);

    private static byte[] Movie(params byte[][] tracks) => Box("moov", Concat([Box("mvhd", new byte[100]), .. tracks]));

    private static byte[] Track(string handlerType, string sampleFormat)
    {
        byte[] handler = [0, 0, 0, 0, 0, 0, 0, 0, .. Encoding.ASCII.GetBytes(handlerType), .. new byte[13]];
        byte[] sampleDescription = [0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 16, .. Encoding.ASCII.GetBytes(sampleFormat), 0, 0, 0, 0, 0, 0, 0, 1];
        var sampleTable = Box("stbl", Box("stsd", sampleDescription));
        var media = Box("mdia", Concat(Box("mdhd", new byte[24]), Box("hdlr", handler), Box("minf", sampleTable)));
        return Box("trak", Concat(Box("tkhd", new byte[84]), media));
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
