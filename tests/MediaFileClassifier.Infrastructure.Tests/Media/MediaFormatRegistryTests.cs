using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Media.Formats;
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
        var exception = Assert.Throws<InvalidOperationException>(() => new MediaFormatRegistry([new JpegFormatHandler(), new JpegFormatHandler()]));

        Assert.Contains("jpeg", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_SharedExtension_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new MediaFormatRegistry([new JpegFormatHandler(), new FakeFormatHandler("fake", ".jpg")]));

        Assert.Contains(".jpg", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_NewHandler_IsDetectedWithoutOtherChanges()
    {
        // Spec 1.11: a new format is one more handler.
        var registry = new MediaFormatRegistry([new JpegFormatHandler(), new FakeFormatHandler("fake", ".fak")]);

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

        public int SignatureLength => 4;

        public bool MatchesSignature(ReadOnlySpan<byte> header) => header.StartsWith("FAKE"u8);

        public ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken)
            => ValueTask.FromResult(FormatInspection.Supported(MediaType.Image));
    }
}
