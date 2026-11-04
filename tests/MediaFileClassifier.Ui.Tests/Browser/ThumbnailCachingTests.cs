using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Tests for the decoded thumbnail cache, the result pages and a tile's thumbnail (E14-S01-T02, T03).
/// </summary>
public sealed class ThumbnailCachingTests
{
    // A 64 × 64 bitmap takes 16 KB.
    private const long BitmapBytes = 64 * 64 * 4;

    [Fact]
    public Task BitmapCache_EvictsTheLeastRecentlyUsedOverItsBudget() => TestApp.RunAsync(() =>
    {
        var decoded = new List<string>();
        using var cache = new ThumbnailBitmapCache(2 * BitmapBytes, 2, path =>
        {
            lock (decoded)
            {
                decoded.Add(path);
            }

            return NewBitmap();
        });

        Load(cache, "a")?.Dispose();
        Load(cache, "b")?.Dispose();
        Load(cache, "a")?.Dispose();
        Load(cache, "c")?.Dispose();
        Load(cache, "a")?.Dispose();
        Load(cache, "b")?.Dispose();

        // "b" was the least recently used when "c" came in, so only it was decoded twice.
        Assert.Equal(["a", "b", "c", "b"], decoded);
        Assert.Equal(2 * BitmapBytes, cache.UsedBytes);
    });

    [Fact]
    public Task BitmapCache_KeepsAnEvictedBitmapWhileATileHoldsIt() => TestApp.RunAsync(() =>
    {
        using var cache = new ThumbnailBitmapCache(BitmapBytes, 1, _ => NewBitmap());
        using var held = Load(cache, "a")!;

        Load(cache, "b")?.Dispose();

        Assert.Equal(new PixelSize(64, 64), held.Bitmap.PixelSize);
        Assert.Equal(BitmapBytes, cache.UsedBytes);
    });

    [Fact]
    public Task BitmapCache_DecodesAgainWhenTheFileChanged_AndReportsUndecodableFiles() => TestApp.RunAsync(() =>
    {
        var decodes = 0;
        using var cache = new ThumbnailBitmapCache(10 * BitmapBytes, 1, path =>
        {
            Interlocked.Increment(ref decodes);
            return path == "broken" ? null : NewBitmap();
        });

        Load(cache, "a", version: 1)?.Dispose();
        Load(cache, "a", version: 1)?.Dispose();
        Load(cache, "a", version: 2)?.Dispose();

        Assert.Equal(2, decodes);
        Assert.Null(Load(cache, "broken"));
    });

    [Fact]
    public void Pages_LoadOnceAndStayWithinTheirBound()
    {
        var source = new FakeResultSource(100_000);
        var pages = new ResultPageCache(source);

        pages.Request(0, 399);
        pages.Request(0, 399);
        Assert.Equal(2, source.PageRequests);
        Assert.Equal(source.Ids[250], pages.Get(250)?.Id);

        for (var page = 0; page < 100; page++)
        {
            pages.Request(page * ResultPageCache.PageSize, page * ResultPageCache.PageSize);
        }

        Assert.Equal(ResultPageCache.MaxPages, pages.LoadedPageCount);
        Assert.Null(pages.Get(0));
        Assert.Equal(source.Ids[99 * ResultPageCache.PageSize], pages.Get(99 * ResultPageCache.PageSize)?.Id);
    }

    [Fact]
    public void Pages_AfterInvalidate_ShowTheOldResultsShiftedUntilTheyReload()
    {
        var source = new FakeResultSource(1_000);
        var pages = new ResultPageCache(source);
        pages.Request(0, 10);
        var first = pages.Get(0)!.Id;

        source.InsertAtTop(3);
        pages.Invalidate(shift: 3);

        Assert.Equal(first, pages.Get(3)?.Id);
        pages.Request(0, 10);
        Assert.Equal(source.Ids[0], pages.Get(0)?.Id);
        Assert.Equal(first, pages.Get(3)?.Id);
    }

    [Fact]
    public Task Tile_NeverShowsTheThumbnailOfAnItemItNoLongerShows() => TestApp.RunAsync(() =>
    {
        var gates = new Dictionary<string, ManualResetEventSlim> { ["a"] = new(), ["b"] = new() };
        using var bitmaps = new ThumbnailBitmapCache(10 * BitmapBytes, 2, path =>
        {
            gates[path].Wait(TimeSpan.FromSeconds(10));
            return NewBitmap();
        });
        var files = new FakeThumbnailCache();
        var source = new FakeResultSource(2);
        files.Files[(source.Ids[0], ThumbnailSize.Small)] = "a";
        files.Files[(source.Ids[1], ThumbnailSize.Small)] = "b";
        var tile = new ThumbnailTileViewModel(new ThumbnailLoader(new FakeBrowserSession(), files, bitmaps), 160, 120);

        tile.Show(0, FakeResultSource.Summary(source.Ids[0], 0), ThumbnailSize.Small);
        tile.Show(0, FakeResultSource.Summary(source.Ids[1], 1), ThumbnailSize.Small);
        gates["a"].Set();
        Thread.Sleep(50);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(tile.Thumbnail);

        gates["b"].Set();
        Pump.Until(() => tile.Thumbnail is not null);
        Assert.Equal(source.Ids[1], tile.Item?.Id);
    });

    [Theory]
    [InlineData(MediaItemStatus.Discovered, "Pending")]
    [InlineData(MediaItemStatus.Pending, "Pending")]
    [InlineData(MediaItemStatus.Error, "Error")]
    [InlineData(MediaItemStatus.Indexed, null)]
    public Task TileBadges_FollowTheItemStatus(MediaItemStatus status, string? badge) => TestApp.RunAsync(() =>
    {
        var item = FakeResultSource.Summary(Domain.Identity.Id<MediaItem>.New(), 0, status) with { StatusReason = "Decoder failed" };

        var badges = TileBadge.For(item);

        Assert.Equal(badge is null ? [] : [badge], badges.Select(b => b.Kind));
        Assert.All(badges, b => Assert.NotNull(b.Icon));
        if (status == MediaItemStatus.Error)
        {
            Assert.Equal("Error: Decoder failed", badges[0].ToolTip);
        }
    });

    [Theory]
    [InlineData(MediaType.Video, 42, "0:42", false)]
    [InlineData(MediaType.Video, null, null, false)]
    [InlineData(MediaType.AnimatedImage, 3, null, true)]
    [InlineData(MediaType.Image, null, null, false)]
    public Task Tile_ShowsTheDurationOrAnimatedMarker(MediaType mediaType, int? seconds, string? duration, bool animated) => TestApp.RunAsync(() =>
    {
        var tile = new ThumbnailTileViewModel(new ThumbnailLoader(new FakeBrowserSession(), new FakeThumbnailCache(), new ThumbnailBitmapCache()), 160, 120);
        var item = FakeResultSource.Summary(Domain.Identity.Id<MediaItem>.New(), 0) with
        {
            MediaType = mediaType,
            Duration = seconds is { } value ? TimeSpan.FromSeconds(value) : null,
        };

        tile.Show(0, item, ThumbnailSize.Small);

        Assert.Equal(duration, tile.DurationText);
        Assert.Equal(animated, tile.IsAnimated);
        Assert.Equal($"JPG · {Format.FileSize(2048)}", tile.Details);
    });

    [Theory]
    [InlineData(160, 1.0, ThumbnailSize.Small)]
    [InlineData(256, 1.0, ThumbnailSize.Small)]
    [InlineData(288, 1.0, ThumbnailSize.Large)]
    [InlineData(192, 1.5, ThumbnailSize.Large)]
    [InlineData(128, 2.0, ThumbnailSize.Small)]
    public void SizeFor_UsesTheLargeThumbnailOnceTheSmallOneWouldUpscale(double tileWidth, double scaling, ThumbnailSize expected) =>
        Assert.Equal(expected, ThumbnailLoader.SizeFor(tileWidth, scaling));

    private static ThumbnailLease? Load(ThumbnailBitmapCache cache, string path, long version = 0) =>
        cache.LoadAsync(path, version, CancellationToken.None).GetAwaiter().GetResult();

    private static WriteableBitmap NewBitmap() =>
        new(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
}
