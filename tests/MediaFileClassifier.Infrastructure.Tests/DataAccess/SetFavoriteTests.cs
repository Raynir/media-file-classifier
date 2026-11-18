using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for the bulk favorite write of <c>MediaItemRepository</c> (E11-S01-T01).
/// </summary>
public sealed class SetFavoriteTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;
    private List<MediaItem> items = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await LibraryTestBed.CreateAsync();
        items = [.. Enumerable.Range(0, 250).Select(number => MediaItem.Discover(
            bed.Folder.Id, RelativePath.From($"IMG_{number}.jpg"), MediaType.Image, 1, LibraryTestBed.Start, LibraryTestBed.Start, null, LibraryTestBed.Start))];
        await bed.Library.Items.AddRangeAsync(items, Token);
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task SetFavorite_MarksTheItemsAcrossBatches_AndReportsOnlyTheChangedOnes()
    {
        var first = items[0];
        first.SetFavorite(true, LibraryTestBed.Start);
        await bed.Library.Items.UpdateAsync(first, Token);
        var at = LibraryTestBed.Start.AddHours(1);

        var changed = await bed.Library.Items.SetFavoriteAsync([.. items.Select(item => item.Id)], true, at, Token);

        Assert.Equal(items.Count - 1, changed.Count);
        Assert.DoesNotContain(first.Id, changed);
        Assert.All(await bed.Library.Items.GetSummariesByIdsAsync([.. items.Select(item => item.Id)], Token), summary => Assert.True(summary.IsFavorite));
        Assert.Equal(at, (await bed.Library.Items.GetAsync(items[5].Id, Token))!.FavoriteChangedAt);
        Assert.Equal(LibraryTestBed.Start, (await bed.Library.Items.GetAsync(first.Id, Token))!.FavoriteChangedAt);
    }

    [Fact]
    public async Task SetFavorite_Unmarks_AndLeavesOtherColumnsAndUnknownIdsAlone()
    {
        var item = items[3];
        item.SetFavorite(true, LibraryTestBed.Start);
        await bed.Library.Items.UpdateAsync(item, Token);
        var stale = await bed.Library.Items.GetAsync(item.Id, Token);
        var other = await bed.Library.Items.GetAsync(item.Id, Token);
        other!.ChangeStatus(MediaItemStatus.Error, LibraryTestBed.Start.AddMinutes(1));
        await bed.Library.Items.UpdateAsync(other, Token);

        var changed = await bed.Library.Items.SetFavoriteAsync([item.Id, Id<MediaItem>.New()], false, LibraryTestBed.Start.AddHours(1), Token);

        Assert.Equal([item.Id], changed);
        var stored = await bed.Library.Items.GetAsync(item.Id, Token);
        Assert.False(stored!.IsFavorite);
        Assert.Equal(MediaItemStatus.Error, stored.Status);
        Assert.NotNull(stale);
    }

    [Fact]
    public async Task SetFavorite_RaisesChangedOnlyWhenSomethingChanged()
    {
        var raised = 0;
        bed.Library.Items.Changed += (_, _) => raised++;

        await bed.Library.Items.SetFavoriteAsync([items[0].Id], false, LibraryTestBed.Start, Token);
        Assert.Equal(0, raised);

        await bed.Library.Items.SetFavoriteAsync([items[0].Id], true, LibraryTestBed.Start, Token);
        Assert.Equal(1, raised);
    }
}
