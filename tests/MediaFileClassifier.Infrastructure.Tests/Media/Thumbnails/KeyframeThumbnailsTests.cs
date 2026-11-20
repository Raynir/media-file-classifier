using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails;

/// <summary>
/// Tests for <see cref="KeyframeThumbnails"/> (E14-S13-T01, D227): analysis writes a picture per keyframe next to the
/// item's thumbnails, a new analysis replaces them, and deleting an item's thumbnails deletes them too.
/// </summary>
public sealed class KeyframeThumbnailsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"mfc-tests-{Guid.NewGuid():N}.mfc");
    private readonly KeyframeThumbnails pictures = new(NullLogger<KeyframeThumbnails>.Instance);

    public KeyframeThumbnailsTests() => Library = LibraryLayout.FromDirectory(root);

    private LibraryLayout Library { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StoreAsync_WritesOnePictureForEachFrame_InTheItemsShard_ScaledDown()
    {
        var item = Id<MediaItem>.New();

        await pictures.StoreAsync(Library, item, [Frame(0, 640, 360), Frame(1, 640, 360), Frame(2, 100, 80)], Token);

        var id = item.Value.ToString();
        foreach (var index in new[] { 0, 1, 2 })
        {
            Assert.Equal(Path.Combine(root, "thumbnails", id[^2..], $"{id}-frame-{index}.webp"), pictures.Find(Library, item, index));
        }

        using var large = SKBitmap.Decode(pictures.Find(Library, item, 0)!);
        Assert.Equal((192, 108), (large.Width, large.Height));
        using var small = SKBitmap.Decode(pictures.Find(Library, item, 2)!);
        Assert.Equal((100, 80), (small.Width, small.Height));
    }

    [Fact]
    public async Task StoreAsync_AgainWithFewerFrames_ReplacesThePicturesAndRemovesTheExtraOnes()
    {
        var item = Id<MediaItem>.New();
        var other = Id<MediaItem>.New();
        await pictures.StoreAsync(Library, item, [Frame(0, 64, 64), Frame(1, 64, 64), Frame(2, 64, 64)], Token);
        await pictures.StoreAsync(Library, other, [Frame(0, 64, 64)], Token);

        await pictures.StoreAsync(Library, item, [Frame(0, 64, 64)], Token);

        Assert.NotNull(pictures.Find(Library, item, 0));
        Assert.Null(pictures.Find(Library, item, 1));
        Assert.Null(pictures.Find(Library, item, 2));
        Assert.NotNull(pictures.Find(Library, other, 0));
    }

    [Fact]
    public void Find_WithoutPictures_ReturnsNull()
    {
        Assert.Null(pictures.Find(Library, Id<MediaItem>.New(), 0));
    }

    [Fact]
    public async Task StoreAsync_NoFrames_ClearsTheItemsPictures()
    {
        var item = Id<MediaItem>.New();
        await pictures.StoreAsync(Library, item, [Frame(0, 64, 64)], Token);

        await pictures.StoreAsync(Library, item, [], Token);

        Assert.Null(pictures.Find(Library, item, 0));
    }

    [Fact]
    public async Task StoreAsync_WhenTheFolderCantBeWritten_DoesNotThrow()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Library.ThumbnailsDirectory, "a file where the folder should be", Token);

        await pictures.StoreAsync(Library, Id<MediaItem>.New(), [Frame(0, 64, 64)], Token);
    }

    [Fact]
    public async Task Invalidate_DeletesTheKeyframePicturesWithTheThumbnails_AndKeepsOtherItems()
    {
        var item = Id<MediaItem>.New();
        var other = Id<MediaItem>.New();
        await pictures.StoreAsync(Library, item, [Frame(0, 64, 64), Frame(1, 64, 64)], Token);
        await pictures.StoreAsync(Library, other, [Frame(0, 64, 64)], Token);
        using var cache = new ThumbnailCache(TimeProvider.System, NullLogger<ThumbnailCache>.Instance);

        cache.Invalidate(Library, item);

        Assert.Null(pictures.Find(Library, item, 0));
        Assert.Null(pictures.Find(Library, item, 1));
        Assert.NotNull(pictures.Find(Library, other, 0));
    }

    private static SampledFrame Frame(int index, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(i % 251);
            pixels[i + 1] = (byte)(index * 40);
            pixels[i + 2] = 128;
            pixels[i + 3] = 255;
        }

        return new SampledFrame
        {
            Index = index,
            Timestamp = TimeSpan.FromSeconds(index * 10),
            PerceptualHash = 0,
            Width = width,
            Height = height,
            Pixels = pixels,
        };
    }
}
