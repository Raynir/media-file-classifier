using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for <see cref="SearchQuery.RelevanceCut"/> in the search index (E09-S01-T03). The query is (1, 0, 0, 0), so an
/// item's similarity is its first value after normalising.
/// </summary>
public sealed class SearchRelevanceCutTests : IAsyncLifetime
{
    // Centred bar 0.2 over the library mean, raw floor 0.95.
    private static readonly SemanticRelevanceCut Cut = new(0.2f, 0.95f);

    private SearchTestBed bed = null!;

    public async ValueTask InitializeAsync() => bed = await SearchTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task VectorOnly_WeakHits_AreHiddenAndCounted()
    {
        // Similarities 0.9, 0.3, 0 and 0: the mean is 0.3, so only the first clears 0.3 + 0.2.
        var strong = await bed.AddItemAsync(1, vector: [0.9f, 0.43589f, 0, 0]);
        var weak = await bed.AddItemAsync(2, vector: [0.3f, 0.95394f, 0, 0]);
        var none = await bed.AddItemAsync(3, vector: [0, 0, 1, 0]);
        var none2 = await bed.AddItemAsync(4, vector: [0, 0, 0, 1]);

        var hidden = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0), RelevanceCut = Cut });
        var shown = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0), RelevanceCut = Cut, IncludeLessRelevant = true });

        Assert.Equal([strong.Id], hidden.Hits.Select(hit => hit.ItemId));
        Assert.Equal(1, hidden.TotalCount);
        Assert.Equal(3, hidden.LessRelevantCount);
        Assert.False(hidden.Hits[0].IsLessRelevant);

        Assert.Equal(4, shown.TotalCount);
        Assert.Equal(3, shown.LessRelevantCount);
        Assert.Equal([strong.Id, weak.Id], shown.Hits.Take(2).Select(hit => hit.ItemId));
        Assert.Equal([false, true, true, true], shown.Hits.Select(hit => hit.IsLessRelevant));
        Assert.Equivalent(new[] { none.Id, none2.Id }, shown.Hits.Skip(2).Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task NoCut_ShowsEverythingUnmarked()
    {
        await bed.AddItemAsync(1, vector: [0.9f, 0.43589f, 0, 0]);
        await bed.AddItemAsync(2, vector: [0, 0, 1, 0]);

        var results = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0) });

        Assert.Equal(2, results.Hits.Count);
        Assert.Equal(0, results.LessRelevantCount);
        Assert.DoesNotContain(results.Hits, hit => hit.IsLessRelevant);
    }

    [Fact]
    public async Task Hybrid_KeywordMatchWithAWeakVector_IsNeverHidden()
    {
        var titled = await bed.AddItemAsync(1, title: "beach", vector: [0, 0, 1, 0]);
        var strong = await bed.AddItemAsync(2, vector: [0.9f, 0.43589f, 0, 0]);
        var weak = await bed.AddItemAsync(3, vector: [0, 0, 0, 1]);

        var results = await bed.SearchAsync(new SearchQuery { Text = "beach", Vector = bed.Vector(1, 0, 0, 0), RelevanceCut = Cut });

        Assert.Equivalent(new[] { titled.Id, strong.Id }, results.Hits.Select(hit => hit.ItemId));
        Assert.DoesNotContain(results.Hits, hit => hit.ItemId == weak.Id);
        Assert.Equal(1, results.LessRelevantCount);
        Assert.Equal(2, results.TotalCount);
    }

    [Fact]
    public async Task OneSubjectLibrary_RawFloorKeepsStrongMatches()
    {
        // Everything is close to the query, so every centred similarity is tiny; the raw floor keeps them all.
        var items = new[]
        {
            await bed.AddItemAsync(1, vector: [0.99f, 0.14107f, 0, 0]),
            await bed.AddItemAsync(2, vector: [0.98f, 0, 0.19900f, 0]),
            await bed.AddItemAsync(3, vector: [0.97f, 0, 0, 0.24310f]),
        };

        var results = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0), RelevanceCut = Cut });

        Assert.Equal(items.Select(item => item.Id), results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(0, results.LessRelevantCount);
    }

    [Fact]
    public async Task OtherSortAndPaging_CountOnlyTheRelevantHits()
    {
        // Mean of 0.9, 0.85, 0.1, 0 and 0 is 0.37: the first two clear 0.57.
        var b = await bed.AddItemAsync(1, vector: [0.9f, 0.43589f, 0, 0]);
        var a = await bed.AddItemAsync(2, vector: [0.85f, 0.52678f, 0, 0]);
        await bed.AddItemAsync(3, vector: [0.1f, 0.99499f, 0, 0]);
        await bed.AddItemAsync(4, vector: [0, 0, 1, 0]);
        await bed.AddItemAsync(5, vector: [0, 0, 0, 1]);

        var byName = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0), RelevanceCut = Cut, Sort = SortMode.Name });
        var secondPage = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0), RelevanceCut = Cut, Offset = 1, Limit = 1 });

        Assert.Equal([b.Id, a.Id], byName.Hits.Select(hit => hit.ItemId)); // IMG_00001 before IMG_00002.
        Assert.Equal(2, byName.TotalCount);
        Assert.Equal(3, byName.LessRelevantCount);
        Assert.Equal([a.Id], secondPage.Hits.Select(hit => hit.ItemId));
        Assert.Equal(2, secondPage.TotalCount);
    }

    [Fact]
    public async Task Cut_NotFinite_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => bed.SearchAsync(new SearchQuery
        {
            Vector = bed.Vector(1, 0, 0, 0),
            RelevanceCut = new SemanticRelevanceCut(float.NaN, 0.1f),
        }));
    }
}
