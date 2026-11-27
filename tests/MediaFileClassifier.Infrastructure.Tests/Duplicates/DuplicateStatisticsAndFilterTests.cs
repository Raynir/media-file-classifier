using System.Diagnostics;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;
using MediaFileClassifier.Infrastructure.Tests.Search;
using MediaFileClassifier.Testing.Timing;

namespace MediaFileClassifier.Infrastructure.Tests.Duplicates;

/// <summary>
/// The open-group figures and the Has duplicates filter, against a real library (E12-S05-T01, T02). Item <c>n</c>
/// is <c>1000 + n</c> bytes, so a group's wasted space is easy to work out.
/// </summary>
public sealed class DuplicateStatisticsAndFilterTests : IAsyncLifetime
{
    private SearchTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IDuplicateGroupRepository Duplicates => bed.Library.Library.Duplicates;

    public async ValueTask InitializeAsync() => bed = await SearchTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Statistics_WithoutGroups_AreZero()
    {
        await AddAsync((1, 1), (2, 2));

        Assert.Equal(DuplicateStatistics.None, await Duplicates.GetOpenStatisticsAsync(Token));
    }

    [Fact]
    public async Task Statistics_CountOpenGroups_AndAllMembersButTheLargest()
    {
        // Group of 3 (1001, 1002, 1003): 2003 wasted. Group of 2 (1010, 1011): 1010 wasted.
        await AddAsync((1, 7), (2, 7), (3, 7), (10, 8), (11, 8), (20, 9));
        await ReconcileAsync();

        Assert.Equal(new DuplicateStatistics(2, 2003 + 1010), await Duplicates.GetOpenStatisticsAsync(Token));
    }

    [Theory]
    [InlineData(DuplicateGroupState.Ignored)]
    [InlineData(DuplicateGroupState.Resolved)]
    public async Task Statistics_LeaveOutGroupsThatAreNotOpen(DuplicateGroupState state)
    {
        await AddAsync((1, 7), (2, 7), (10, 8), (11, 8));
        await ReconcileAsync();
        var group = (await Duplicates.GetGroupsAsync(null, 0, 10, Token)).First(group => group.ContentHash == 7);
        await Duplicates.ChangeStateAsync(group.Id, state, bed.Library.Clock.Tick(), Token);

        Assert.Equal(new DuplicateStatistics(1, 1010), await Duplicates.GetOpenStatisticsAsync(Token));
    }

    [Fact]
    public async Task Statistics_FollowTheGroups_WhenAnItemLeaves()
    {
        var items = await AddAsync((1, 7), (2, 7), (3, 7));
        await ReconcileAsync();
        await bed.Library.Library.Items.DeleteAsync(items[2].Id, Token);
        await ReconcileAsync();

        Assert.Equal(new DuplicateStatistics(1, 1001), await Duplicates.GetOpenStatisticsAsync(Token));
    }

    [Fact]
    public async Task HasDuplicates_KeepsExactlyTheMembersOfOpenGroups()
    {
        var items = await AddAsync((1, 7), (2, 7), (10, 8), (11, 8), (20, 9));
        await ReconcileAsync();
        await IgnoreAsync(8);

        var found = await SearchAsync(new BooleanValue(true));

        Assert.Equal(Ids(items[0], items[1]), Ids(found));
    }

    [Fact]
    public async Task HasDuplicates_Off_FiltersNothing()
    {
        var items = await AddAsync((1, 7), (2, 7), (20, 9));
        await ReconcileAsync();

        Assert.Equal(Ids(items), Ids(await SearchAsync(new BooleanValue(false))));
    }

    [Fact]
    public async Task HasDuplicates_AfterTheGroupIsIgnored_FindsNothing()
    {
        await AddAsync((1, 7), (2, 7));
        await ReconcileAsync();
        await IgnoreAsync(7);

        Assert.Empty(await SearchAsync(new BooleanValue(true)));
    }

    [Fact]
    public async Task HasDuplicates_20kItems_StaysFast()
    {
        const int Count = 20_000;
        var items = Enumerable.Range(1, Count).Select(n => bed.Library.NewItem(n)).ToList();
        for (var i = 0; i < Count; i++)
        {
            // Every tenth item pairs with the next: 2,000 groups of two.
            var hash = i % 10 < 2 ? (UInt128)(i / 10) + 1 : (UInt128)i + 100_000;
            items[i].SetHashes(items[i].Hashes with { ContentHash = hash });
        }

        await bed.Library.Library.Items.AddRangeAsync(items, Token);
        await ReconcileAsync();

        var clock = Stopwatch.StartNew();
        var found = await SearchResultsAsync(new BooleanValue(true));
        var elapsed = clock.Elapsed;
        TestContext.Current.SendDiagnosticMessage($"Has duplicates at {Count:N0} items: {found.TotalCount:N0} hits in {elapsed.TotalMilliseconds:F0} ms.");

        Assert.Equal(4_000, found.TotalCount);
        if (TestPerformance.Enabled)
        {
            Assert.True(elapsed < TimeSpan.FromSeconds(1), elapsed.ToString());
        }
    }

    private static string[] Ids(IEnumerable<Id<MediaItem>> ids) => [.. ids.Select(id => id.ToString()).Order(StringComparer.Ordinal)];

    private static string[] Ids(params MediaItem[] items) => Ids(items.Select(item => item.Id));

    private static string[] Ids(List<MediaItem> items) => Ids(items.Select(item => item.Id));

    private async Task<List<MediaItem>> AddAsync(params (int Number, int Hash)[] specs)
    {
        var items = specs.Select(spec =>
        {
            var item = bed.Library.NewItem(spec.Number);
            item.SetHashes(item.Hashes with { ContentHash = (UInt128)spec.Hash });
            return item;
        }).ToList();
        await bed.Library.Library.Items.AddRangeAsync(items, Token);
        return items;
    }

    private async Task ReconcileAsync()
    {
        while (await Duplicates.ReconcileAsync(1000, bed.Library.Clock.Tick(), Token) is { Hashes: > 0 })
        {
        }
    }

    private async Task IgnoreAsync(int hash)
    {
        var group = (await Duplicates.GetGroupsAsync(null, 0, 100, Token)).First(group => group.ContentHash == (UInt128)hash);
        await Duplicates.ChangeStateAsync(group.Id, DuplicateGroupState.Ignored, bed.Library.Clock.Tick(), Token);
    }

    private async Task<List<Id<MediaItem>>> SearchAsync(BooleanValue value) =>
        [.. (await SearchResultsAsync(value)).Hits.Select(hit => hit.ItemId)];

    private Task<SearchResults> SearchResultsAsync(BooleanValue value) =>
        bed.SearchAsync(new SearchQuery { Filter = new SearchFilter { Conditions = [new FilterCondition(new HasDuplicatesFilter(), value)] } });
}
