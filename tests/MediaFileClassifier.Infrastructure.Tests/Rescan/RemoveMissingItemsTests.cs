using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Rescan.RemoveMissingItems;
using MediatR;

namespace MediaFileClassifier.Infrastructure.Tests.Rescan;

/// <summary>
/// Finding and removing the items whose files are gone, with a real library (E02-S04-T04).
/// </summary>
public sealed class RemoveMissingItemsTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync();
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "b/alpha.png");
        bed.Copy(SampleMedia.WebP, "webp.webp");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task MissingFilter_FindsOnlyMissingItems_AndTheCountMatches()
    {
        File.Delete(Path.Combine(bed.Root, "webp.webp"));
        File.Delete(Path.Combine(bed.Root, "b/alpha.png"));
        await ReconcilerTests.RescanAsync(bed);

        var filter = new MediaItemFilter { Status = MediaItemStatus.Missing };
        var found = await bed.Library.Items.GetSummariesAsync(filter, 0, 50, Token);

        Assert.Equal(["alpha.png", "webp.webp"], found.Select(item => item.FileName).Order(StringComparer.Ordinal));
        Assert.Equal(2, await bed.Library.Items.CountAsync(filter, Token));
        Assert.Equal(2, await bed.Get<ISender>().Send(new CountMissingItemsQuery(), Token));
    }

    [Fact]
    public async Task RemoveMissing_DeletesOnlyMissingItems_AndKeepsTheFiles()
    {
        var present = Path.Combine(bed.Root, "a/photo.jpg");
        File.Delete(Path.Combine(bed.Root, "webp.webp"));
        await ReconcilerTests.RescanAsync(bed);

        var removed = await bed.Get<ISender>().Send(new RemoveMissingItemsCommand(), Token);

        Assert.Equal(1, removed);
        Assert.Equal(["a/photo.jpg", "b/alpha.png"], (await bed.ItemsAsync()).Select(item => item.Path.Value).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(present));
        Assert.Equal(0, await bed.Get<ISender>().Send(new CountMissingItemsQuery(), Token));
    }

    [Fact]
    public async Task RemoveMissing_NothingMissing_RemovesNothing()
    {
        var removed = await bed.Get<ISender>().Send(new RemoveMissingItemsCommand(), Token);

        Assert.Equal(0, removed);
        Assert.Equal(3, (await bed.ItemsAsync()).Count);
    }

    [Fact]
    public async Task DeleteMissing_RespectsTheLimit_AndLeavesOtherItems()
    {
        File.Delete(Path.Combine(bed.Root, "webp.webp"));
        await ReconcilerTests.RescanAsync(bed);

        var deleted = await bed.Library.Items.DeleteMissingAsync(1, Token);

        Assert.Single(deleted);
        Assert.Empty(await bed.Library.Items.DeleteMissingAsync(10, Token));
        Assert.Equal(2, (await bed.ItemsAsync()).Count);
    }
}
