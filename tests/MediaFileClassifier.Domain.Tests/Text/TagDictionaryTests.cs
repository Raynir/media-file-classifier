using System.Text.Json;
using MediaFileClassifier.Domain.Text;

namespace MediaFileClassifier.Domain.Tests.Text;

/// <summary>
/// Tests for <see cref="TagDictionary"/> (D348, E06-S04-T09).
/// </summary>
public sealed class TagDictionaryTests
{
    [Theory]
    [InlineData("cat", "кошка")]
    [InlineData("Dog", "собака")]
    [InlineData("  black   and white ", "чёрно-белый")]
    [InlineData("dogs", "собака")]
    [InlineData("berries", "ягода")]
    [InlineData("leaves", "лист")]
    [InlineData("blue skies", "голубое небо")]
    public void Translate_EnglishTag_GivesTheRussianForm(string tag, string expected) =>
        Assert.Equal(expected, TagDictionary.Default.Translate(tag));

    [Theory]
    [InlineData("кошка", "cat")]
    [InlineData("кошки", "cat")]
    [InlineData("Собаку", "dog")]
    [InlineData("голубого неба", "blue sky")]
    [InlineData("пляжи", "beach")]
    [InlineData("новогодние елки", "christmas tree")]
    [InlineData("чёрно-белая", "black and white")]
    public void Translate_RussianTag_IsMatchedByStem(string tag, string expected) =>
        Assert.Equal(expected, TagDictionary.Default.Translate(tag));

    [Fact]
    public void Translate_RussianFormsSharingAStem_AreFoundOnlyAsWritten()
    {
        var dictionary = new TagDictionary([("taxi", "такси"), ("dachshund", "такса")]);

        Assert.Equal("taxi", dictionary.Translate("такси"));
        Assert.Equal("dachshund", dictionary.Translate("такса"));
        Assert.Null(dictionary.Translate("таксой"));
    }

    [Fact]
    public void Translate_SynonymsWithOneRussianForm_GiveTheFirstEnglishTag()
    {
        var dictionary = new TagDictionary([("pigeon", "голубь"), ("dove", "голубь")]);

        Assert.Equal("голубь", dictionary.Translate("dove"));
        Assert.Equal("pigeon", dictionary.Translate("голуби"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("flux capacitor")]
    [InlineData("бутерброд с инопланетянином")]
    public void Translate_UnknownTag_GivesNothing(string? tag) =>
        Assert.Null(TagDictionary.Default.Translate(tag));

    [Fact]
    public void Translate_FormSameAsTheTag_GivesNothing() =>
        Assert.Null(new TagDictionary([("кафе", "кафе")]).Translate("Кафе"));

    [Theory]
    [InlineData("ёж")]
    [InlineData("еж")]
    [InlineData("ЕЖИ")]
    public void Translate_TreatsYoAsYe(string tag) =>
        Assert.Equal("hedgehog", TagDictionary.Default.Translate(tag));

    [Fact]
    public void Constructor_EntryWithoutRussian_Throws() =>
        Assert.Throws<ArgumentException>(() => new TagDictionary([("wi-fi", "wi-fi")]));

    [Fact]
    public void Default_EveryEntryTranslatesBothWays()
    {
        var entries = ReadEntries();
        var dictionary = TagDictionary.Default;

        Assert.True(dictionary.Count >= 1500, $"{dictionary.Count} entries");
        var oneWay = entries.Where(entry => dictionary.Translate(entry.English) is null).Select(entry => entry.English).ToList();
        var noWayBack = entries
            .Where(entry => dictionary.Translate(dictionary.Translate(entry.Russian)) != entry.Russian)
            .Select(entry => entry.Russian)
            .ToList();
        Assert.Empty(oneWay);
        Assert.Empty(noWayBack);
    }

    [Fact]
    public void Default_EnglishTagsAreLowerCaseAndUnique()
    {
        var english = ReadEntries().Select(entry => entry.English).ToList();

        Assert.All(english, tag => Assert.Equal(tag.Trim().ToLowerInvariant(), tag));
        Assert.Equal(english.Count, english.Distinct(StringComparer.Ordinal).Count());
    }

    private static List<(string English, string Russian)> ReadEntries()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Text", "tag-dictionary.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.GetProperty("entries").EnumerateArray().Select(entry => (entry[0].GetString()!, entry[1].GetString()!))];
    }
}
