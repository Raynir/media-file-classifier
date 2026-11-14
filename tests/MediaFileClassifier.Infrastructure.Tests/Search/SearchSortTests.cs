using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for the sort modes of the search index (E10-S04-T01): natural name order, stable paging in every order, and
/// sort keys that follow the items' changes.
/// </summary>
public sealed class SearchSortTests : IAsyncLifetime
{
    private SearchTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<SortMode, bool> Orders()
    {
        var data = new TheoryData<SortMode, bool>();
        foreach (var sort in Enum.GetValues<SortMode>())
        {
            data.Add(sort, false);
            data.Add(sort, true);
        }

        return data;
    }

    public async ValueTask InitializeAsync() => bed = await SearchTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Name_IsNatural_InEveryPath()
    {
        var names = new[] { "b/IMG_10.jpg", "IMG_2.jpg", "a/img_1.jpg", "Photo 3.jpg" };
        var items = new List<MediaItem>();
        for (var i = 0; i < names.Length; i++)
        {
            var path = names[i];
            items.Add(await bed.AddItemAsync(i + 1, title: "sample", vector: [1, 0, 0, 0], configure: item => item.Relocate(item.FolderId, RelativePath.From(path))));
        }

        // img_1, IMG_2, IMG_10, Photo 3: numbers in numeric order, case and folders ignored.
        Id<MediaItem>[] expected = [items[2].Id, items[1].Id, items[0].Id, items[3].Id];
        foreach (var query in new[]
        {
            new SearchQuery { Sort = SortMode.Name },
            new SearchQuery { Text = "sample", Sort = SortMode.Name },
            new SearchQuery { Vector = bed.Vector(1, 0, 0, 0), Sort = SortMode.Name },
            new SearchQuery { Text = "sample", Vector = bed.Vector(1, 0, 0, 0), Sort = SortMode.Name },
        })
        {
            Assert.Equal(expected, (await bed.SearchAsync(query)).Hits.Select(hit => hit.ItemId));
            Assert.Equal([.. expected.Reverse()], (await bed.SearchAsync(query with { ReverseSort = true })).Hits.Select(hit => hit.ItemId));
        }
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task Paging_ShowsEveryItemOnce_InTheOrderOfOnePage(SortMode sort, bool reverse)
    {
        // Few distinct sizes, dates and names, so most items tie on their sort key and only the document ID orders them.
        var items = Enumerable.Range(1, 60).Select(number =>
        {
            var item = bed.Library.NewItem(number);
            item.Relocate(item.FolderId, RelativePath.From($"f{number}/IMG_{number % 4}.jpg"));
            item.UpdateFile(1000 + (number % 3), item.FileCreatedAt, LibraryTestBed.Start.AddDays(-(number % 6)), item.Hashes);
            return item;
        }).ToList();
        await bed.Library.Library.Items.AddRangeAsync(items, Token);

        var whole = await bed.SearchAsync(new SearchQuery { Sort = sort, ReverseSort = reverse, Limit = 1000 });
        var paged = new List<Id<MediaItem>>();
        for (var offset = 0; ; offset += 7)
        {
            var page = await bed.SearchAsync(new SearchQuery { Sort = sort, ReverseSort = reverse, Offset = offset, Limit = 7 });
            Assert.Equal(60, page.TotalCount);
            if (page.Hits.Count == 0)
            {
                break;
            }

            paged.AddRange(page.Hits.Select(hit => hit.ItemId));
        }

        Assert.Equal(60, whole.TotalCount);
        Assert.Equal(60, paged.Distinct().Count());
        Assert.Equal(whole.Hits.Select(hit => hit.ItemId), paged);

        var byId = items.ToDictionary(item => item.Id);
        var keys = paged.Select(id => byId[id]).Select(item => sort switch
        {
            SortMode.Name => (double)(item.Path.Value[^5] - '0'),
            SortMode.Size => item.SizeBytes,
            SortMode.Date => item.FileModifiedAt.ToUnixTimeMilliseconds(),
            _ => 0,
        }).ToList();
        var natural = sort == SortMode.Name ? keys.Order() : keys.OrderDescending();
        Assert.Equal(reverse ? natural.Reverse() : natural, keys);
    }

    [Fact]
    public async Task Keys_FollowRenamesFileChangesAndDatesTaken()
    {
        var first = await bed.AddItemAsync(1);
        var second = await bed.AddItemAsync(2);
        Assert.Equal([first.Id, second.Id], await OrderAsync(SortMode.Name));
        Assert.Equal([second.Id, first.Id], await OrderAsync(SortMode.Size));
        Assert.Equal([first.Id, second.Id], await OrderAsync(SortMode.Date));

        first.Relocate(first.FolderId, RelativePath.From("album/IMG_00100.jpg"));
        first.UpdateFile(1, first.FileCreatedAt, first.FileModifiedAt, first.Hashes);
        await bed.Library.Library.Items.UpdateAsync(first, Token);
        Assert.Equal([second.Id, first.Id], await OrderAsync(SortMode.Name));
        Assert.Equal([second.Id, first.Id], await OrderAsync(SortMode.Size));

        // A date taken wins over the file date; removing the technical metadata goes back to the file date.
        await bed.Library.Library.Items.UpdateAsync(
            second,
            new TechnicalMetadata { ItemId = second.Id, DateTaken = LibraryTestBed.Start.UtcDateTime, DateTakenOffset = TimeSpan.Zero, ExtractedAt = LibraryTestBed.Start },
            Token);
        Assert.Equal([second.Id, first.Id], await OrderAsync(SortMode.Date));

        await bed.Library.Library.Items.UpdateAsync(
            second,
            new TechnicalMetadata { ItemId = second.Id, DateTaken = LibraryTestBed.Start.UtcDateTime.AddYears(-1), DateTakenOffset = TimeSpan.FromHours(3), ExtractedAt = LibraryTestBed.Start },
            Token);
        Assert.Equal([first.Id, second.Id], await OrderAsync(SortMode.Date));
    }

    [Fact]
    public async Task ExcludedItems_AreNeverListed()
    {
        var kept = await bed.AddItemAsync(1);
        var excluded = await bed.AddItemAsync(2);
        excluded.ChangeStatus(MediaItemStatus.Excluded, LibraryTestBed.Start);
        await bed.Library.Library.Items.UpdateAsync(excluded, Token);

        foreach (var sort in Enum.GetValues<SortMode>())
        {
            var results = await bed.SearchAsync(new SearchQuery { Sort = sort });
            Assert.Equal([kept.Id], results.Hits.Select(hit => hit.ItemId));
            Assert.Equal(1, results.TotalCount);
        }
    }

    private async Task<List<Id<MediaItem>>> OrderAsync(SortMode sort) =>
        [.. (await bed.SearchAsync(new SearchQuery { Sort = sort })).Hits.Select(hit => hit.ItemId)];
}
