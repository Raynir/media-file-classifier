using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for the "favorites only" filter (E11-S01-T04): it keeps favorites, combines with text search and the other
/// filters, and reaches the keyword, vector and list paths alike.
/// </summary>
public sealed class FavoriteFilterTests : IAsyncLifetime
{
    private SearchTestBed bed = null!;
    private List<MediaItem> items = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await SearchTestBed.CreateAsync();
        items =
        [
            await bed.AddItemAsync(1, "sunset beach", [1f, 0f, 0f, 0f], item => item.SetFavorite(true, bed.Library.Clock.Tick())),
            await bed.AddItemAsync(2, "sunset city", [1f, 0f, 0f, 0f]),
            await bed.AddItemAsync(3, "forest", [0f, 1f, 0f, 0f], item => item.SetFavorite(true, bed.Library.Clock.Tick())),
        ];
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Theory]
    [InlineData(true, new[] { 0, 2 })]
    [InlineData(false, new[] { 1 })]
    public async Task ListSearch_KeepsFavoritesOrTheRest(bool value, int[] expected)
    {
        var results = await bed.SearchAsync(new SearchQuery { Filter = Filter(value) });

        Assert.Equal(Ids(expected), results.Hits.Select(hit => hit.ItemId).Order(IdComparer.Instance));
    }

    [Fact]
    public async Task KeywordSearch_CombinesTheTextWithTheFilter()
    {
        var results = await bed.SearchAsync(new SearchQuery { Text = "sunset", Filter = Filter(true) });

        Assert.Equal([items[0].Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task VectorSearch_ScoresOnlyFavorites()
    {
        var results = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0), Filter = Filter(true) });

        Assert.DoesNotContain(items[1].Id, results.Hits.Select(hit => hit.ItemId));
        Assert.Contains(items[0].Id, results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task Filter_CombinesWithTheOtherFilters()
    {
        var filter = new SearchFilter
        {
            Conditions = [new FilterCondition(new FavoriteFilter(), new BooleanValue(true))],
            MediaTypes = [MediaType.Video],
        };

        var results = await bed.SearchAsync(new SearchQuery { Filter = filter });

        Assert.Empty(results.Hits);
    }

    private static SearchFilter Filter(bool value) => new() { Conditions = [new FilterCondition(new FavoriteFilter(), new BooleanValue(value))] };

    private List<Id<MediaItem>> Ids(int[] indexes) => [.. indexes.Select(index => items[index].Id).Order(IdComparer.Instance)];

    private sealed class IdComparer : IComparer<Id<MediaItem>>
    {
        public static IdComparer Instance { get; } = new();

        public int Compare(Id<MediaItem> x, Id<MediaItem> y) => x.Value.CompareTo(y.Value);
    }
}
