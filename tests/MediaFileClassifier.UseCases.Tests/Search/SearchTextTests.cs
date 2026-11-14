using MediaFileClassifier.UseCases.Search.SearchMedia;

namespace MediaFileClassifier.UseCases.Tests.Search;

/// <summary>
/// Tests for <see cref="SearchText"/> (E09-S01-T02).
/// </summary>
public sealed class SearchTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" \t\r\n ", "")]
    [InlineData("  red   car  ", "red car")]
    [InlineData("red car boat", "red car boat")]
    [InlineData("red\tcar\nboat", "red car boat")]
    [InlineData("re​d ca﻿r", "red car")]
    [InlineData("‎‏", "")]
    [InlineData("a\0b", "a b")]
    [InlineData("\"quoted\" AND (x OR y*) NEAR:col^", "\"quoted\" AND (x OR y*) NEAR:col^")]
    [InlineData("Ёлка", "Ёлка")]
    [InlineData("猫 🐱", "猫 🐱")]
    public void Normalise_CleansWhiteSpaceAndInvisibleCharacters(string? text, string expected) =>
        Assert.Equal(expected, SearchText.Normalise(text));

    [Fact]
    public void Normalise_CombiningMarks_BecomePrecomposed()
    {
        var decomposed = "ëlka café";

        Assert.Equal("ëlka café", SearchText.Normalise(decomposed));
    }

    [Fact]
    public void Normalise_LoneSurrogate_IsReplacedNotThrown()
    {
        var result = SearchText.Normalise("cat\uD800dog");

        Assert.Equal("cat�dog", result);
    }

    [Fact]
    public void Normalise_LongText_IsCutWithoutSplittingACharacter()
    {
        var text = new string('a', SearchText.MaxLength - 1) + "🐱🐱";

        var result = SearchText.Normalise(text);

        Assert.Equal(SearchText.MaxLength - 1, result.Length);
        Assert.DoesNotContain('\uD83D', result);
    }

    [Fact]
    public void Normalise_LongText_DoesNotEndWithASpace()
    {
        var text = new string('a', SearchText.MaxLength) + " more";

        var result = SearchText.Normalise(text);

        Assert.Equal(SearchText.MaxLength, result.Length);
        Assert.False(char.IsWhiteSpace(result[^1]));
    }
}
