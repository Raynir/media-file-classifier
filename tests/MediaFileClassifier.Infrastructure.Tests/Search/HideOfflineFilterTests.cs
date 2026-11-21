using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for the "Hide offline items" filter (E14-S07-T03): turned on, it leaves out exactly the items of Offline
/// folders, in the list, keyword and vector paths alike; turned off, it filters nothing.
/// </summary>
public sealed class HideOfflineFilterTests : IAsyncLifetime
{
    private SearchTestBed bed = null!;
    private IndexedFolder offline = null!;
    private List<MediaItem> items = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await SearchTestBed.CreateAsync();
        var library = bed.Library.Library;
        offline = IndexedFolder.Create(bed.Library.Folder.LibraryId, @"E:\Backup", @"E:\Backup", bed.Library.Clock.Tick());
        await library.Folders.AddAsync(offline, Token);

        // A third folder that went offline and came back counts as online.
        var back = IndexedFolder.Create(bed.Library.Folder.LibraryId, @"F:\Card", @"F:\Card", bed.Library.Clock.Tick());
        back.MarkOffline(bed.Library.Clock.Tick());
        back.MarkOnline(bed.Library.Clock.Tick());
        await library.Folders.AddAsync(back, Token);

        items =
        [
            await bed.AddItemAsync(1, "sunset beach", [1f, 0f, 0f, 0f]),
            await bed.AddItemAsync(2, "sunset city", [1f, 0f, 0f, 0f], item => item.Relocate(offline.Id, RelativePath.From("city.jpg"))),
            await bed.AddItemAsync(3, "forest", [0f, 1f, 0f, 0f], item => item.Relocate(back.Id, RelativePath.From("forest.jpg"))),
            await bed.AddItemAsync(4, "sunset lake", [1f, 0f, 0f, 0f], item => item.Relocate(offline.Id, RelativePath.From("lake.jpg"))),
        ];

        offline.MarkOffline(bed.Library.Clock.Tick());
        await library.Folders.UpdateAsync(offline, Token);
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task ListSearch_On_HidesExactlyTheOfflineItems()
    {
        var results = await bed.SearchAsync(new SearchQuery { Filter = Filter(true) });

        Assert.Equal(Ids(0, 2), results.Hits.Select(hit => hit.ItemId).Order(IdComparer.Instance));
    }

    [Fact]
    public async Task ListSearch_Off_FiltersNothing()
    {
        var results = await bed.SearchAsync(new SearchQuery { Filter = Filter(false) });

        Assert.Equal(Ids(0, 1, 2, 3), results.Hits.Select(hit => hit.ItemId).Order(IdComparer.Instance));
    }

    [Fact]
    public async Task KeywordSearch_On_HidesTheOfflineMatches()
    {
        var results = await bed.SearchAsync(new SearchQuery { Text = "sunset", Filter = Filter(true) });

        Assert.Equal([items[0].Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task VectorSearch_On_HidesTheOfflineMatches()
    {
        var results = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0), Filter = Filter(true) });

        var hits = results.Hits.Select(hit => hit.ItemId).ToList();
        Assert.Contains(items[0].Id, hits);
        Assert.DoesNotContain(items[1].Id, hits);
        Assert.DoesNotContain(items[3].Id, hits);
    }

    [Fact]
    public async Task ListSearch_On_ShowsTheItemsAgainOnceTheFolderIsBack()
    {
        offline.MarkOnline(bed.Library.Clock.Tick());
        await bed.Library.Library.Folders.UpdateAsync(offline, Token);

        var results = await bed.SearchAsync(new SearchQuery { Filter = Filter(true) });

        Assert.Equal(Ids(0, 1, 2, 3), results.Hits.Select(hit => hit.ItemId).Order(IdComparer.Instance));
    }

    private static SearchFilter Filter(bool value) => new() { Conditions = [new FilterCondition(new HideOfflineFilter(), new BooleanValue(value))] };

    private List<Id<MediaItem>> Ids(params int[] indexes) => [.. indexes.Select(index => items[index].Id).Order(IdComparer.Instance)];

    private sealed class IdComparer : IComparer<Id<MediaItem>>
    {
        public static IdComparer Instance { get; } = new();

        public int Compare(Id<MediaItem> x, Id<MediaItem> y) => x.Value.CompareTo(y.Value);
    }
}
