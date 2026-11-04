using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Search.Ranking;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for <see cref="ReciprocalRankFusion"/> on fixed ranked lists (E01-S04-T04).
/// </summary>
public sealed class ReciprocalRankFusionTests
{
    [Fact]
    public void Fuse_ScoresEachDocumentOverBothLists()
    {
        var fused = ReciprocalRankFusion.Fuse([1, 2, 3], [3, 4, 1], SearchRankingOptions.Default);

        // 1: 1/61 + 1/63, 3: 1/63 + 1/61, 2: 1/62, 4: 1/62. Ties go to the better single rank, then to the keyword list.
        Assert.Equal([1, 3, 2, 4], fused.Select(hit => hit.DocumentId));
        Assert.Equal((1.0 / 61) + (1.0 / 63), fused[0].Score, 12);
        Assert.Equal((1, 3), (fused[0].KeywordRank!.Value, fused[0].VectorRank!.Value));
        Assert.Equal((3, 1), (fused[1].KeywordRank!.Value, fused[1].VectorRank!.Value));
        Assert.Equal((2, (int?)null), (fused[2].KeywordRank!.Value, fused[2].VectorRank));
        Assert.Equal(((int?)null, 2), (fused[3].KeywordRank, fused[3].VectorRank!.Value));
    }

    [Fact]
    public void Fuse_DocumentInBothLists_BeatsTopOfOneList()
    {
        var fused = ReciprocalRankFusion.Fuse([10, 20, 30], [40, 50, 30], SearchRankingOptions.Default);

        Assert.Equal(30, fused[0].DocumentId);
    }

    [Fact]
    public void Fuse_Weights_ShiftTheOrder()
    {
        var vectorHeavy = ReciprocalRankFusion.Fuse([1, 2], [2, 3], SearchRankingOptions.Default with { VectorWeight = 3 });
        var keywordOnly = ReciprocalRankFusion.Fuse([1, 2], [3, 4], SearchRankingOptions.Default with { VectorWeight = 0 });

        Assert.Equal([2, 3, 1], vectorHeavy.Select(hit => hit.DocumentId));
        Assert.Equal([1, 2, 3, 4], keywordOnly.Select(hit => hit.DocumentId));
        Assert.Equal(0, keywordOnly[2].Score);
    }

    [Fact]
    public void Fuse_SmallK_RewardsTopRanksMore()
    {
        // With k = 60, rank 1 in one list (1/61) loses to rank 2 in both (2/62). With k = 0, rank 1 in one list (1/1)
        // ties with rank 2 in both (1/2 + 1/2) and wins on its better single rank.
        var flat = ReciprocalRankFusion.Fuse([1, 2], [3, 2], SearchRankingOptions.Default);
        var steep = ReciprocalRankFusion.Fuse([1, 2], [3, 2], SearchRankingOptions.Default with { RrfK = 0 });

        Assert.Equal(2, flat[0].DocumentId);
        Assert.Equal([1, 3, 2], steep.Select(hit => hit.DocumentId));
    }

    [Fact]
    public void Fuse_EmptyLists_GiveTheOtherList()
    {
        Assert.Equal([5, 6], ReciprocalRankFusion.Fuse([], [5, 6], SearchRankingOptions.Default).Select(hit => hit.DocumentId));
        Assert.Equal([7, 8], ReciprocalRankFusion.Fuse([7, 8], [], SearchRankingOptions.Default).Select(hit => hit.DocumentId));
        Assert.Empty(ReciprocalRankFusion.Fuse([], [], SearchRankingOptions.Default));
    }

    [Fact]
    public void Fuse_EqualScoresAndRanks_PreferTheKeywordList()
    {
        var fused = ReciprocalRankFusion.Fuse([9], [4], SearchRankingOptions.Default with { KeywordWeight = 1, VectorWeight = 1 });

        Assert.Equal([9, 4], fused.Select(hit => hit.DocumentId));
    }

    [Theory]
    [InlineData(-1, 1, 1, 10)]
    [InlineData(60, -1, 1, 10)]
    [InlineData(60, 1, -1, 10)]
    [InlineData(60, 1, 1, 0)]
    public void Fuse_InvalidOptions_Throw(double k, double keywordWeight, double vectorWeight, int candidates)
    {
        var options = new SearchRankingOptions { RrfK = k, KeywordWeight = keywordWeight, VectorWeight = vectorWeight, CandidateLimit = candidates };

        Assert.Throws<ArgumentOutOfRangeException>(() => ReciprocalRankFusion.Fuse([1], [2], options));
    }

    [Theory]
    [InlineData("  red   car ", new[] { "red", "car" })]
    [InlineData("Red red RED", new[] { "Red" })]
    [InlineData("- \" ** IMG_0042", new[] { "IMG_0042" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void QueryTerms_SplitOnWhiteSpace_AndDropWordsThatCantMatch(string? text, string[] expected)
    {
        Assert.Equal(expected, QueryTerms.Parse(text));
    }
}
