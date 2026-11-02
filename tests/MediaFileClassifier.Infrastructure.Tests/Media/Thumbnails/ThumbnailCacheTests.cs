using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using MediaFileClassifier.Testing.Media;
using Microsoft.Extensions.DependencyInjection;
using static MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails.ThumbnailTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails;

/// <summary>
/// Tests for <see cref="ThumbnailCache"/> (E03-S04-T04).
/// </summary>
public sealed class ThumbnailCacheTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"mfc-tests-{Guid.NewGuid():N}.mfc");
    private readonly ServiceProvider services = CreateServices(videoAnalysis: false);

    public ThumbnailCacheTests() => Library = LibraryLayout.FromDirectory(root);

    private LibraryLayout Library { get; }

    private IThumbnailCache Cache => services.GetRequiredService<IThumbnailCache>();

    public void Dispose()
    {
        services.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateAsync_Image_StoresBothSizesInTheItemsShard()
    {
        var content = Content(SampleMedia.WebP);

        var outcome = await GenerateAsync(content, "webp.webp");

        Assert.True(outcome.IsStored);
        Assert.Empty(outcome.Warnings);
        var id = content.ItemId.Value.ToString();
        foreach (var size in new[] { 256, 512 })
        {
            Assert.True(File.Exists(Path.Combine(root, "thumbnails", id[^2..], $"{id}-{size}.webp")));
        }
    }

    [Fact]
    public async Task Find_AfterGenerate_ReturnsThePathOfEachSize()
    {
        var content = Content(SampleMedia.JpegWithExif);
        await GenerateAsync(content, "jpeg-exif.jpg");

        Assert.Equal(Library.GetThumbnailPath(content.ItemId, ThumbnailSize.Small), Cache.Find(Library, content.ItemId, ThumbnailSize.Small, Modified));
        Assert.Equal(Library.GetThumbnailPath(content.ItemId, ThumbnailSize.Large), Cache.Find(Library, content.ItemId, ThumbnailSize.Large, Modified));
    }

    [Fact]
    public void Find_NoThumbnail_ReturnsNull()
    {
        Assert.Null(Cache.Find(Library, Id<MediaItem>.New(), ThumbnailSize.Small, Modified));
    }

    [Fact]
    public async Task Find_FileChangedAfterTheThumbnail_ReturnsNull()
    {
        var content = Content(SampleMedia.WebP);
        await GenerateAsync(content, "webp.webp");

        Assert.Null(Cache.Find(Library, content.ItemId, ThumbnailSize.Small, DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public async Task GenerateAsync_FileTimeInTheFuture_IsNotStale()
    {
        var future = DateTimeOffset.UtcNow.AddDays(30);
        var content = Content(SampleMedia.WebP) with { LastModifiedUtc = future };

        await GenerateAsync(content, "webp.webp");

        Assert.NotNull(Cache.Find(Library, content.ItemId, ThumbnailSize.Small, future));
    }

    [Fact]
    public async Task Invalidate_DeletesBothSizes()
    {
        var content = Content(SampleMedia.WebP);
        await GenerateAsync(content, "webp.webp");

        Cache.Invalidate(Library, content.ItemId);

        Assert.Null(Cache.Find(Library, content.ItemId, ThumbnailSize.Small, Modified));
        Assert.Null(Cache.Find(Library, content.ItemId, ThumbnailSize.Large, Modified));
        Assert.Empty(Directory.GetFiles(Library.ThumbnailsDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Invalidate_NoThumbnail_DoesNothing()
    {
        Cache.Invalidate(Library, Id<MediaItem>.New());
    }

    [Fact]
    public async Task GenerateAsync_Failure_DeletesTheOldThumbnailsAndReturnsTheWarning()
    {
        var content = Content(SampleMedia.WebP);
        await GenerateAsync(content, "webp.webp");
        var broken = Content(SampleMedia.ZeroByteFile) with { ItemId = content.ItemId };
        var format = MediaFormatDetection.Supported(FormatTestSupport.CreateRegistry().FindByFormatId("webp")!, MediaType.Image, []);

        var outcome = await Cache.GenerateAsync(Library, broken, format, new() { ItemId = content.ItemId, ExtractedAt = Modified }, TestContext.Current.CancellationToken);

        Assert.False(outcome.IsStored);
        Assert.Equal(ThumbnailWarnings.ThumbnailFailed, Assert.Single(outcome.Warnings).Code);
        Assert.Null(Cache.Find(Library, content.ItemId, ThumbnailSize.Small, Modified));
    }

    [Fact]
    public async Task GenerateAsync_VideoWithoutFfmpeg_StoresNothingAndWarns()
    {
        var content = Content(SampleMedia.ShortVideo);

        var outcome = await GenerateAsync(content, "video-short.mp4");

        Assert.False(outcome.IsStored);
        Assert.Equal(ThumbnailWarnings.ThumbnailFfmpegUnavailable, Assert.Single(outcome.Warnings).Code);
    }

    [Fact]
    public async Task GenerateAsync_Regenerate_ReplacesTheThumbnails()
    {
        var content = Content(SampleMedia.WebP);
        await GenerateAsync(content, "webp.webp");
        var other = Content(SampleMedia.JpegWithExif) with { ItemId = content.ItemId };

        await GenerateAsync(other, "jpeg-exif.jpg");

        Assert.Equal((384, 512), SizeOf(await File.ReadAllBytesAsync(Library.GetThumbnailPath(content.ItemId, ThumbnailSize.Large), TestContext.Current.CancellationToken)));
        Assert.Equal(2, Directory.GetFiles(Library.ThumbnailsDirectory, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task GenerateAsync_ManyAtOnce_AllComplete()
    {
        var contents = Enumerable.Range(0, 3 * ThumbnailCache.MaxConcurrency).Select(_ => Content(SampleMedia.JpegWithExif)).ToList();

        var outcomes = await Task.WhenAll(contents.Select(content => GenerateAsync(content, "jpeg-exif.jpg")));

        Assert.All(outcomes, outcome => Assert.True(outcome.IsStored));
    }

    [Fact]
    public async Task GenerateAsync_UnsupportedFormat_Throws()
    {
        var content = Content(SampleMedia.AudioOnlyMp4);
        var format = MediaFormatDetection.Rejected(new(MediaFormatIssues.NoVideoTrack));

        await Assert.ThrowsAsync<ArgumentException>(
            () => Cache.GenerateAsync(Library, content, format, new() { ItemId = content.ItemId, ExtractedAt = Modified }, TestContext.Current.CancellationToken));
    }

    private async Task<ThumbnailOutcome> GenerateAsync(Abstractions.Metadata.MediaContent content, string fileName)
    {
        var (format, metadata) = await InspectAsync(services, content, fileName);
        return await Cache.GenerateAsync(Library, content, format, metadata, TestContext.Current.CancellationToken);
    }
}
