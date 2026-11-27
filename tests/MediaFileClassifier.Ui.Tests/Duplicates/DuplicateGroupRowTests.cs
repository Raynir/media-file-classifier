using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.ViewModels.Duplicates;

namespace MediaFileClassifier.Ui.Tests.Duplicates;

/// <summary>
/// Tests for the thumbnail strip of <see cref="DuplicateGroupRowViewModel"/> (E12-S03-T01): loaded when the row is
/// shown, released when it scrolls away.
/// </summary>
public sealed class DuplicateGroupRowTests
{
    [Fact]
    public Task LoadStrip_FillsTheStripOnce_AndUnloadReleasesIt() => TestApp.RunAsync(async () =>
    {
        using var cache = new ThumbnailBitmapCache(1024 * 1024, 1, _ => new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul));
        var calls = 0;
        var row = new DuplicateGroupRowViewModel(
            new DuplicateGroupSummary(Id<DuplicateGroup>.New(), DuplicateGroupType.Exact, 2, 10, DateTimeOffset.UnixEpoch),
            TestLocalizer.EnUs(),
            async (_, token) =>
            {
                calls++;
                return [await cache.LoadAsync("a", 1, token) ?? throw new InvalidOperationException(), await cache.LoadAsync("b", 1, token) ?? throw new InvalidOperationException()];
            });

        await row.LoadStripAsync();
        await row.LoadStripAsync();

        Assert.Equal(2, row.Strip.Count);
        Assert.Equal(1, calls);

        row.Unload();

        Assert.Empty(row.Strip);
        await row.LoadStripAsync();
        Assert.Equal(2, calls);
    });

    [Fact]
    public async Task LoadStrip_WithoutASource_LeavesTheStripEmpty()
    {
        var row = new DuplicateGroupRowViewModel(
            new DuplicateGroupSummary(Id<DuplicateGroup>.New(), DuplicateGroupType.Exact, 2, 10, DateTimeOffset.UnixEpoch),
            TestLocalizer.EnUs());

        await row.LoadStripAsync();

        Assert.Empty(row.Strip);
    }
}
