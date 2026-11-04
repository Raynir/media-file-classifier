using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Formats;
using MediaFileClassifier.Testing.Media;
using static MediaFileClassifier.Infrastructure.Tests.Media.FormatTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media;

/// <summary>
/// Tests for <see cref="MediaFormatRegistry"/> and the MVP format registrations.
/// </summary>
public sealed class MediaFormatRegistryTests
{
    [Fact]
    public void AddMedia_RegistersTheMvpFormats()
    {
        var registry = CreateRegistry();

        Assert.Equal(["jpeg", "png", "webp", "gif", "mp4"], registry.Handlers.Select(handler => handler.FormatId));
        Assert.Equal("image/jpeg", registry.FindByFormatId("jpeg")?.MimeType);
        Assert.Equal("video/mp4", registry.FindByFormatId("MP4")?.MimeType);
        Assert.Null(registry.FindByFormatId("heic"));
    }

    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("photo.JPEG", true)]
    [InlineData("dir/sub/clip.m4v", true)]
    [InlineData("anim.webp", true)]
    [InlineData("notes.txt", false)]
    [InlineData("photo.heic", false)]
    [InlineData("clip.mov", false)]
    [InlineData("no-extension", false)]
    public void IsCandidate_FiltersByExtension(string fileName, bool expected)
    {
        Assert.Equal(expected, CreateRegistry().IsCandidate(fileName));
    }

    [Fact]
    public void Constructor_DuplicateFormat_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new MediaFormatRegistry([new JpegFormatHandler(ImageExtractor(), ImageThumbnails()), new JpegFormatHandler(ImageExtractor(), ImageThumbnails())]));

        Assert.Contains("jpeg", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_SharedExtension_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new MediaFormatRegistry([new JpegFormatHandler(ImageExtractor(), ImageThumbnails()), new FakeFormatHandler("fake", ".jpg")]));

        Assert.Contains(".jpg", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("jpeg-exif.jpg", "jpeg", MediaType.Image)]
    [InlineData("фото с пробелом.jpg", "jpeg", MediaType.Image)]
    [InlineData("png-alpha.png", "png", MediaType.Image)]
    [InlineData("ocr-text.png", "png", MediaType.Image)]
    [InlineData("webp.webp", "webp", MediaType.Image)]
    [InlineData("gif-static.gif", "gif", MediaType.Image)]
    [InlineData("video-short.mp4", "mp4", MediaType.Video)]
    public async Task DetectAsync_SampleCorpus_DetectsFormatAndMediaType(string sample, string formatId, MediaType mediaType)
    {
        var detection = await DetectFileAsync(Path.Combine(SampleMedia.Directory, sample));

        Assert.True(detection.IsSupported, detection.Rejection?.ToString());
        Assert.Equal(formatId, detection.Handler?.FormatId);
        Assert.Equal(mediaType, detection.MediaType);
        Assert.Empty(detection.Warnings);
    }

    [Fact]
    public async Task DetectAsync_JpegNamedPng_IsJpegWithMismatchWarning()
    {
        var detection = await DetectFileAsync(SampleMedia.JpegWithExif, "holiday.png");

        Assert.True(detection.IsSupported);
        Assert.Equal("jpeg", detection.Handler?.FormatId);
        Assert.Equal(MediaType.Image, detection.MediaType);
        Assert.Equal([new MediaFormatIssue(MediaFormatIssues.ExtensionMismatch, "jpeg")], detection.Warnings);
    }

    [Fact]
    public async Task DetectAsync_AnimatedGifNamedWebP_KeepsItsMediaType()
    {
        var detection = await DetectFileAsync(SampleMedia.AnimatedGif, "anim.webp");

        Assert.Equal("gif", detection.Handler?.FormatId);
        Assert.Equal(MediaType.AnimatedImage, detection.MediaType);
        Assert.Equal(MediaFormatIssues.ExtensionMismatch, Assert.Single(detection.Warnings).Code);
    }

    [Fact]
    public async Task DetectAsync_TextNamedJpg_IsRejected()
    {
        var detection = await DetectBytesAsync("This is not a photo.\r\n"u8.ToArray(), "photo.jpg");

        Assert.False(detection.IsSupported);
        Assert.Null(detection.Handler);
        Assert.Equal(MediaFormatIssues.UnrecognizedContent, detection.Rejection?.Code);
    }

    [Fact]
    public async Task DetectAsync_ContentShorterThanEverySignature_IsRejected()
    {
        var detection = await DetectBytesAsync([0xFF, 0xD8], "photo.jpg");

        Assert.Equal(MediaFormatIssues.UnrecognizedContent, detection.Rejection?.Code);
    }

    [Fact]
    public async Task DetectAsync_ZeroByteFile_IsRejectedAsEmpty()
    {
        var detection = await DetectFileAsync(SampleMedia.ZeroByteFile);

        Assert.Equal(MediaFormatIssues.EmptyFile, detection.Rejection?.Code);
    }

    [Fact]
    public async Task DetectAsync_UnregisteredExtension_IsRejectedWithoutReading()
    {
        var content = new ThrowingStream();

        var detection = await CreateRegistry().DetectAsync(content, "notes.TXT", TestContext.Current.CancellationToken);

        Assert.Equal(new MediaFormatIssue(MediaFormatIssues.UnsupportedExtension, ".txt"), detection.Rejection);
    }

    [Fact]
    public async Task DetectAsync_CorruptJpeg_IsDetectedAsJpeg()
    {
        // Decoding fails later; detection only needs the signature.
        var detection = await DetectFileAsync(SampleMedia.CorruptJpeg);

        Assert.True(detection.IsSupported);
        Assert.Equal("jpeg", detection.Handler?.FormatId);
    }

    [Fact]
    public async Task DetectAsync_ReadError_IsRejectedAsUnreadable()
    {
        var detection = await CreateRegistry().DetectAsync(new ThrowingStream(), "photo.jpg", TestContext.Current.CancellationToken);

        Assert.Equal(new MediaFormatIssue(MediaFormatIssues.Unreadable, ThrowingStream.Message), detection.Rejection);
    }

    [Theory]
    [InlineData("gif-animated.gif", MediaType.AnimatedImage)]
    [InlineData("webp-animated.webp", MediaType.AnimatedImage)]
    [InlineData("video-short.mp4", MediaType.Video)]
    [InlineData("jpeg-exif.jpg", MediaType.Image)]
    public async Task DetectAsync_StreamThatCannotSeek_GivesTheSameResult(string sample, MediaType mediaType)
    {
        var detection = await DetectFileAsync(Path.Combine(SampleMedia.Directory, sample), seekable: false);

        Assert.True(detection.IsSupported, detection.Rejection?.ToString());
        Assert.Equal(mediaType, detection.MediaType);
    }

    [Fact]
    public async Task DetectAsync_NewHandler_IsDetectedWithoutOtherChanges()
    {
        // Spec 1.11: a new format is one more handler.
        var registry = new MediaFormatRegistry([new JpegFormatHandler(ImageExtractor(), ImageThumbnails()), new FakeFormatHandler("fake", ".fak")]);

        var detection = await registry.DetectAsync(new MemoryStream("FAKE content"u8.ToArray()), "x.fak", TestContext.Current.CancellationToken);

        Assert.True(detection.IsSupported);
        Assert.Equal("fake", detection.Handler?.FormatId);
        Assert.True(registry.IsCandidate("y.FAK"));
    }

    private sealed class FakeFormatHandler(string formatId, string extension) : IMediaFormatHandler
    {
        public string FormatId => formatId;

        public string MimeType => "application/x-fake";

        public IReadOnlyList<string> Extensions => [extension];

        public IMediaMetadataExtractor MetadataExtractor => ImageExtractor();

        public IThumbnailGenerator ThumbnailGenerator => ImageThumbnails();

        public int SignatureLength => 4;

        public bool MatchesSignature(ReadOnlySpan<byte> header) => header.StartsWith("FAKE"u8);

        public ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken)
            => ValueTask.FromResult(FormatInspection.Supported(MediaType.Image));
    }

    private sealed class ThrowingStream : Stream
    {
        public const string Message = "The device is not ready.";

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException(Message);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new IOException(Message);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
