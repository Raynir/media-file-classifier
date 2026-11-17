using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Tags;
using MediaFileClassifier.UseCases.Tests.Libraries;

namespace MediaFileClassifier.UseCases.Tests.Tags;

/// <summary>
/// Tests for <see cref="TagNormalizer"/> (E06-S04-T05).
/// </summary>
public sealed class TagNormalizerTests
{
    [Fact]
    public void Normalize_CaseWhitespaceAndPlural_EndUpAsOneTag()
    {
        var tags = new TagNormalizer().Normalize(["Dogs", "dog", " Dog "]);

        Assert.Equal(["dog"], Values(tags));
    }

    [Fact]
    public void Normalize_GenericTags_AreRemoved()
    {
        var tags = new TagNormalizer().Normalize(["Photo", "photos", "image", "beach", "Фотография", "картинки"]);

        Assert.Equal(["beach"], Values(tags));
    }

    [Theory]
    [InlineData("Boxes", "box")]
    [InlineData("berries", "berry")]
    [InlineData("leaves", "leaf")]
    [InlineData("children", "child")]
    [InlineData("mice", "mouse")]
    [InlineData("red cars", "red car")]
    [InlineData("golden_retrievers", "golden retriever")]
    [InlineData("#sunset.", "sunset")]
    [InlineData("- trees", "tree")]
    [InlineData("Hot Dogs", "Hot Dog")]
    [InlineData("photos of cats", "photos of cat")]
    public void Normalize_EnglishTag_BecomesSingularAndClean(string raw, string expected)
    {
        var tags = new TagNormalizer().Normalize([raw]);

        Assert.Equal([expected], Values(tags));
    }

    [Theory]
    [InlineData("glasses")]
    [InlineData("jeans")]
    [InlineData("news")]
    [InlineData("bus")]
    [InlineData("grass")]
    [InlineData("people")]
    [InlineData("USA")]
    [InlineData("iPhone")]
    [InlineData("Eiffel Tower")]
    [InlineData("собаки")]
    [InlineData("3D")]
    [InlineData("panda")]
    [InlineData("pasta")]
    [InlineData("soda")]
    [InlineData("tennis")]
    [InlineData("cactus")]
    [InlineData("dallas")]
    [InlineData("wales")]
    [InlineData("C#")]
    [InlineData(".NET")]
    [InlineData("-5°C")]
    public void Normalize_WordsThatMustNotChange_StayAsWritten(string raw)
    {
        var tags = new TagNormalizer().Normalize([raw]);

        Assert.Equal([raw], Values(tags));
    }

    [Fact]
    public void Normalize_LowersOnlyALeadingCapital()
    {
        var tags = new TagNormalizer().Normalize(["Собака", "Beach", "New York"]);

        Assert.Equal(["собака", "beach", "New York"], Values(tags));
    }

    [Fact]
    public void Normalize_Synonyms_AreMergedIntoOneTag()
    {
        var tags = new TagNormalizer().Normalize(["car", "Automobiles", "kitty", "cat"]);

        Assert.Equal(["car", "cat"], Values(tags));
    }

    [Fact]
    public void Normalize_StopwordsAndSynonymKeys_GoThroughTheSameRules()
    {
        var settings = new FakeSettingsService();
        settings.Save(new AppSettings
        {
            Tags = new TagSettings
            {
                Stopwords = ["Photos", "media"],
                Synonyms = new Dictionary<string, string> { ["Automobiles"] = "Cars" },
            },
        });

        var tags = new TagNormalizer(settings).Normalize(["photo", "photos", "media", "automobile", "beach"]);

        Assert.Equal(["car", "beach"], Values(tags));
    }

    [Fact]
    public void Normalize_EmptyAndNullTags_AreSkipped()
    {
        var tags = new TagNormalizer().Normalize([null, " ", "\"\"", "sea"]);

        Assert.Equal(["sea"], Values(tags));
    }

    [Fact]
    public void Normalize_SettingsLists_ReplaceTheDefaults()
    {
        var settings = new FakeSettingsService();
        settings.Save(new AppSettings
        {
            Tags = new TagSettings
            {
                Stopwords = ["beach"],
                Synonyms = new Dictionary<string, string> { ["sea"] = "ocean" },
            },
        });

        var tags = new TagNormalizer(settings).Normalize(["photo", "beach", "seas", "automobile"]);

        Assert.Equal(["photo", "ocean", "automobile"], Values(tags));
    }

    [Fact]
    public void Normalize_ChangedSettings_ApplyToTheNextCall()
    {
        var settings = new FakeSettingsService();
        var normalizer = new TagNormalizer(settings);
        normalizer.Normalize(["photo"]);

        settings.Save(new AppSettings { Tags = new TagSettings { Stopwords = [] } });

        Assert.Equal(["photo"], Values(normalizer.Normalize(["photo"])));
    }

    private static string[] Values(IEnumerable<Domain.Metadata.TagName> tags) => [.. tags.Select(tag => tag.Value)];
}
