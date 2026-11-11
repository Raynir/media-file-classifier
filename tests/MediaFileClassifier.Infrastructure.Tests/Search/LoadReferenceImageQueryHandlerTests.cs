using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Search.LoadReferenceImage;
using static MediaFileClassifier.Infrastructure.Tests.Media.FormatTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for <see cref="LoadReferenceImageQueryHandler"/> with the real MVP formats (E09-S04-T01, D010).
/// </summary>
public sealed class LoadReferenceImageQueryHandlerTests
{
    private readonly LoadReferenceImageQueryHandler handler = new(CreateRegistry());

    [Theory]
    [InlineData("jpeg-exif.jpg", "image/jpeg")]
    [InlineData("png-alpha.png", "image/png")]
    [InlineData("webp.webp", "image/webp")]
    [InlineData("gif-static.gif", "image/gif")]
    [InlineData("gif-animated.gif", "image/gif")]
    [InlineData("webp-animated.webp", "image/webp")]
    public async Task Load_MvpImage_IsAccepted(string file, string mimeType)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(SampleMedia.Directory, file), TestContext.Current.CancellationToken);

        var load = await LoadAsync(bytes, file);

        Assert.Equal(ReferenceImageProblem.None, load.Problem);
        Assert.NotNull(load.Image);
        Assert.Equal(file, load.Image.Name);
        Assert.Equal(mimeType, load.Image.Image.MediaType);
        Assert.Equal(bytes, load.Image.Image.Data.ToArray());
    }

    [Theory]
    [InlineData("image.jfif")]
    [InlineData("image")]
    [InlineData("download.png")]
    public async Task Load_NameDoesNotMatchTheContent_TheContentDecides(string name)
    {
        var bytes = await File.ReadAllBytesAsync(SampleMedia.JpegWithExif, TestContext.Current.CancellationToken);

        var load = await LoadAsync(bytes, name);

        Assert.Equal(ReferenceImageProblem.None, load.Problem);
        Assert.Equal("image/jpeg", load.Image!.Image.MediaType);
    }

    [Theory]
    [InlineData("video-short.mp4")]
    [InlineData("audio-only.mp4")]
    public async Task Load_NotAnImage_IsUnsupported(string file)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(SampleMedia.Directory, file), TestContext.Current.CancellationToken);

        var load = await LoadAsync(bytes, file);

        Assert.Equal(ReferenceImageProblem.UnsupportedFormat, load.Problem);
        Assert.Null(load.Image);
    }

    [Fact]
    public async Task Load_TextNamedLikeAnImage_IsUnsupported()
    {
        var load = await LoadAsync("not a picture"u8.ToArray(), "notes.png");

        Assert.Equal(ReferenceImageProblem.UnsupportedFormat, load.Problem);
    }

    [Fact]
    public async Task Load_Empty_IsUnreadable()
    {
        var load = await LoadAsync([], "zero-byte.png");

        Assert.Equal(ReferenceImageProblem.Unreadable, load.Problem);
    }

    [Fact]
    public async Task Load_LargerThanTheLimit_IsTooLarge()
    {
        var load = await LoadAsync(new byte[LoadReferenceImageQuery.MaxBytes + 1], "huge.jpg");

        Assert.Equal(ReferenceImageProblem.TooLarge, load.Problem);
    }

    private Task<ReferenceImageLoad> LoadAsync(byte[] content, string name) =>
        handler.Handle(new LoadReferenceImageQuery(content, name), TestContext.Current.CancellationToken);
}
