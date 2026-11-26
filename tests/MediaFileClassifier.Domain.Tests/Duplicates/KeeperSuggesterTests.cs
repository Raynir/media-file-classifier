using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Tests.Duplicates;

/// <summary>
/// Tests for <see cref="KeeperSuggester"/> and <see cref="KeeperRules"/> on synthetic groups (E12-S03-T03).
/// </summary>
public sealed class KeeperSuggesterTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Suggest_TheMemberWithTheMostUserMetadata_BeatsEveryLaterRule()
    {
        var tagged = Candidate(metadata: 3, pixels: 100, size: 10);
        var bigger = Candidate(metadata: 0, pixels: 9000, size: 9000);

        var suggestion = KeeperSuggester.Suggest([bigger, tagged]);

        Assert.Equal(tagged.ItemId, suggestion!.ItemId);
        Assert.Equal(KeeperRule.MostUserMetadata, suggestion.DecidingRule);
    }

    [Fact]
    public void Suggest_WhenMetadataTies_TheHighestResolutionWins()
    {
        var small = Candidate(pixels: 2016 * 1512, size: 5000);
        var large = Candidate(pixels: 4032 * 3024, size: 100);

        var suggestion = KeeperSuggester.Suggest([small, large]);

        Assert.Equal(large.ItemId, suggestion!.ItemId);
        Assert.Equal(KeeperRule.HighestResolution, suggestion.DecidingRule);
    }

    [Fact]
    public void Suggest_WhenResolutionTies_TheLargestFileWins()
    {
        var compressed = Candidate(size: 1_200_000);
        var original = Candidate(size: 8_100_000);

        var suggestion = KeeperSuggester.Suggest([compressed, original]);

        Assert.Equal(original.ItemId, suggestion!.ItemId);
        Assert.Equal(KeeperRule.LargestFile, suggestion.DecidingRule);
    }

    [Fact]
    public void Suggest_WhenSizeTies_TheOriginalFormatWinsOverAConvertedOne()
    {
        var converted = Candidate(format: ".webp");
        var original = Candidate(format: ".JPG");

        var suggestion = KeeperSuggester.Suggest([converted, original]);

        Assert.Equal(original.ItemId, suggestion!.ItemId);
        Assert.Equal(KeeperRule.OriginalFormat, suggestion.DecidingRule);
    }

    [Fact]
    public void Suggest_WhenEverythingElseTies_TheOldestFileWins()
    {
        var newer = Candidate(created: Start.AddDays(10));
        var older = Candidate(created: Start);

        var suggestion = KeeperSuggester.Suggest([newer, older]);

        Assert.Equal(older.ItemId, suggestion!.ItemId);
        Assert.Equal(KeeperRule.Oldest, suggestion.DecidingRule);
    }

    [Fact]
    public void Suggest_IdenticalMembers_TakesOneWithoutARule()
    {
        var first = Candidate();
        var second = Candidate();

        var suggestion = KeeperSuggester.Suggest([first, second]);

        Assert.Null(suggestion!.DecidingRule);
        Assert.Contains(suggestion.ItemId, new[] { first.ItemId, second.ItemId });
    }

    [Fact]
    public void Suggest_WithAConfiguredOrder_TheFirstRuleDecides()
    {
        var oldest = Candidate(created: Start, size: 100);
        var largest = Candidate(created: Start.AddDays(5), size: 9000);

        var suggestion = KeeperSuggester.Suggest([largest, oldest], [KeeperRule.Oldest, KeeperRule.LargestFile]);

        Assert.Equal(oldest.ItemId, suggestion!.ItemId);
        Assert.Equal(KeeperRule.Oldest, suggestion.DecidingRule);
    }

    [Fact]
    public void Suggest_AMissingOrOfflineMember_CanNotBeTheKeeper()
    {
        var best = Candidate(metadata: 9, available: false);
        var other = Candidate();

        var suggestion = KeeperSuggester.Suggest([best, other]);

        Assert.Equal(other.ItemId, suggestion!.ItemId);
        Assert.Null(suggestion.DecidingRule);
    }

    [Fact]
    public void Suggest_WhenNoMemberIsAvailable_SuggestsNothing()
    {
        Assert.Null(KeeperSuggester.Suggest([Candidate(available: false), Candidate(available: false)]));
        Assert.Null(KeeperSuggester.Suggest([]));
    }

    [Fact]
    public void Suggest_AGroupOfThree_NarrowsByEachRuleInTurn()
    {
        var plain = Candidate(pixels: 9000);
        var tagged = Candidate(metadata: 2, pixels: 100);
        var taggedBigger = Candidate(metadata: 2, pixels: 500);

        var suggestion = KeeperSuggester.Suggest([plain, tagged, taggedBigger]);

        Assert.Equal(taggedBigger.ItemId, suggestion!.ItemId);
        Assert.Equal(KeeperRule.HighestResolution, suggestion.DecidingRule);
    }

    [Fact]
    public void Normalize_NullOrEmpty_GivesTheDefaultOrder()
    {
        Assert.Equal(KeeperRules.Default, KeeperRules.Normalize(null));
        Assert.Equal(KeeperRules.Default, KeeperRules.Normalize([]));
    }

    [Fact]
    public void Normalize_APartialOrderWithRepeats_KeepsItsRulesFirstAndAppendsTheRest()
    {
        var order = KeeperRules.Normalize([KeeperRule.Oldest, KeeperRule.Oldest, KeeperRule.LargestFile, (KeeperRule)99]);

        Assert.Equal(
            [KeeperRule.Oldest, KeeperRule.LargestFile, KeeperRule.MostUserMetadata, KeeperRule.HighestResolution, KeeperRule.OriginalFormat],
            order);
    }

    [Theory]
    [InlineData(".dng", ".heic")]
    [InlineData(".heic", ".jpg")]
    [InlineData(".jpg", ".png")]
    [InlineData(".png", ".webp")]
    [InlineData(".xyz", ".webp")]
    public void FormatRank_RanksTheFirstFormatAsMoreOriginal(string original, string converted) =>
        Assert.True(KeeperRules.FormatRank(original) < KeeperRules.FormatRank(converted));

    [Fact]
    public void FormatRank_IgnoresTheDotAndTheCase() =>
        Assert.Equal(KeeperRules.FormatRank(".jpg"), KeeperRules.FormatRank("JPG"));

    private static KeeperCandidate Candidate(
        int metadata = 0,
        long pixels = 1000,
        long size = 1000,
        string format = ".jpg",
        DateTimeOffset? created = null,
        bool available = true) =>
        new(Id<MediaItem>.New(), metadata, pixels, size, format, created ?? Start, available);
}
