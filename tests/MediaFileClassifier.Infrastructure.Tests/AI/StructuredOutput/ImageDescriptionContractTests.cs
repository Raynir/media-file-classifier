using System.Text.Json;
using MediaFileClassifier.Infrastructure.AI.StructuredOutput;

namespace MediaFileClassifier.Infrastructure.Tests.AI.StructuredOutput;

/// <summary>
/// Tests for <see cref="ImageDescriptionContract"/>: the schema, and validation and repair of answers.
/// </summary>
public sealed class ImageDescriptionContractTests
{
    internal const string ValidJson = """
        {"title":"Dog on a beach","description":"A brown dog runs along the shore. Waves break behind it.","summary":"A dog running on a beach.",
         "tags":["dog","beach"],"objects":["dog","waves"],"scenes":["beach"],"actions":["running"],"emotions":["joyful"]}
        """;

    internal const string ValidJson2 = """
        {"title":"Dog on a beach","title_ru":"Собака на пляже","description":"A brown dog runs along the shore. Waves break behind it.",
         "summary":"A dog running on a beach.","tags":[{"en":"dog","ru":"собака"},{"en":"beach","ru":"пляж"}],
         "objects":["dog","waves"],"scenes":["beach"],"actions":["running"],"emotions":["joyful"]}
        """;

    [Fact]
    public void Schema_LimitsEveryTextAndList_SoAGrammarStopsRunawayOutput()
    {
        var properties = ImageDescriptionContract.GetSchema(1).GetProperty("properties");

        Assert.Equal(ImageDescriptionContract.MaxTitleLength, properties.GetProperty("title").GetProperty("maxLength").GetInt32());
        Assert.Equal(ImageDescriptionContract.MaxDescriptionLength, properties.GetProperty("description").GetProperty("maxLength").GetInt32());
        Assert.Equal(ImageDescriptionContract.MaxSummaryLength, properties.GetProperty("summary").GetProperty("maxLength").GetInt32());
        foreach (var list in new[] { "tags", "objects", "scenes", "actions", "emotions" })
        {
            Assert.Equal(list == "tags" ? ImageDescriptionContract.MaxTagItems : ImageDescriptionContract.MaxListItems, properties.GetProperty(list).GetProperty("maxItems").GetInt32());
            Assert.Equal(ImageDescriptionContract.MaxListItemLength, properties.GetProperty(list).GetProperty("items").GetProperty("maxLength").GetInt32());
        }
    }

    [Fact]
    public void Schema_RequiresEveryFieldAndAllowsNoOthers()
    {
        var schema = ImageDescriptionContract.GetSchema(1);

        var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToList();
        Assert.Equal(["title", "description", "summary", "tags", "objects", "scenes", "actions", "emotions"], required);
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("8 words", schema.GetProperty("properties").GetProperty("title").GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ValidAnswer_ReturnsDescription()
    {
        var result = ImageDescriptionContract.Parse(ValidJson);

        Assert.True(result.IsSuccess);
        Assert.Equal("Dog on a beach", result.Value!.Title);
        Assert.Equal(["dog", "beach"], result.Value.Tags);
        Assert.Equal(["joyful"], result.Value.Emotions);
    }

    [Theory]
    [InlineData("```json\n" + "{\"title\":\"T\",\"description\":\"D\",\"summary\":\"S\"}" + "\n```")]
    [InlineData("Sure! Here is the JSON: {\"title\":\"T\",\"description\":\"D\",\"summary\":\"S\"} Hope this helps.")]
    [InlineData("{\"Title\":\"T\",\"DESCRIPTION\":\"D\",\"summary\":\"S\",}")]
    [InlineData("{\"title\":\"T\",\"description\":\"D\",\"summary\":\"S\",\"people\":[\"x\"],\"tags\":null}")]
    [InlineData("{// comment\n\"title\":\" T \",\"description\":\"D\",\"summary\":\"S\"}")]
    public void Parse_RepairableAnswer_IsRepaired(string raw)
    {
        var result = ImageDescriptionContract.Parse(raw);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("T", result.Value!.Title);
        Assert.Empty(result.Value.Tags);
    }

    [Fact]
    public void Parse_Lists_AreCleanedAndCut()
    {
        var tags = string.Join(",", Enumerable.Range(0, 40).Select(index => $"\"tag{index}\""));
        var raw = $$"""{"title":"T","description":"D","summary":"S","tags":[{{tags}}],"objects":["Dog","dog"," ",null,"{{new string('x', 100)}}"]}""";

        var result = ImageDescriptionContract.Parse(raw);

        Assert.Equal(ImageDescriptionContract.MaxTagItems, result.Value!.Tags.Count);
        Assert.Equal(["Dog", new string('x', ImageDescriptionContract.MaxListItemLength)], result.Value.Objects);
    }

    [Fact]
    public void Parse_OverlongTitle_IsCut()
    {
        var raw = $$"""{"title":"{{new string('t', 500)}}","description":"D","summary":"S"}""";

        Assert.Equal(ImageDescriptionContract.MaxTitleLength, ImageDescriptionContract.Parse(raw).Value!.Title.Length);
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("   ", "empty")]
    [InlineData("I can't describe this image.", "no JSON object")]
    [InlineData("{\"title\":\"T\",\"description\":\"D\"}", "summary")]
    [InlineData("{\"title\":\"\",\"description\":\" \",\"summary\":\"S\"}", "title, description")]
    [InlineData("{\"title\":\"T\",\"description\":\"D\",\"summary\":\"S\",\"tags\":\"dog\"}", "schema")]
    [InlineData("{\"title\":[\"T\"],\"description\":\"D\",\"summary\":\"S\"}", "schema")]
    [InlineData("{\"title\":\"T\",", "no JSON object")]
    [InlineData("{\"title\": \"T\" \"description\"}", "schema")]
    [InlineData("null", "no JSON object")]
    public void Parse_UnusableAnswer_FailsWithReasonWithoutThrowing(string? raw, string reason)
    {
        var result = ImageDescriptionContract.Parse(raw);

        Assert.False(result.IsSuccess);
        Assert.Contains(reason, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Schema_IsValidJson(int schema)
    {
        Assert.Equal(JsonValueKind.Object, ImageDescriptionContract.GetSchema(schema).ValueKind);
    }

    [Fact]
    public void Schema2_IsTheSpikesSchema_WithRussianTitleAndTagPairs()
    {
        var schema = ImageDescriptionContract.GetSchema(2);
        var properties = schema.GetProperty("properties");

        Assert.Equal(
            ["title", "title_ru", "description", "summary", "tags", "objects", "scenes", "actions", "emotions"],
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["title", "title_ru", "description", "summary", "tags", "objects", "scenes", "actions", "emotions"], properties.EnumerateObject().Select(property => property.Name));
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(ImageDescriptionContract.MaxTitleLength, properties.GetProperty("title_ru").GetProperty("maxLength").GetInt32());
        var tags = properties.GetProperty("tags");
        Assert.Equal(ImageDescriptionContract.MaxTagItems, tags.GetProperty("maxItems").GetInt32());
        Assert.Equal(["en", "ru"], tags.GetProperty("items").GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.False(tags.GetProperty("items").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(1, properties.GetProperty("objects").GetProperty("minItems").GetInt32());
        Assert.Equal(1, properties.GetProperty("scenes").GetProperty("minItems").GetInt32());
        Assert.False(properties.GetProperty("actions").TryGetProperty("minItems", out _));
    }

    [Fact]
    public void Parse2_ValidAnswer_ReturnsTheRussianTitleAndTagForms()
    {
        var result = ImageDescriptionContract.Parse(ValidJson2, 2);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Dog on a beach", result.Value!.Title);
        Assert.Equal("Собака на пляже", result.Value.TitleRu);
        Assert.Equal(["dog", "beach"], result.Value.Tags);
        Assert.Equal("собака", result.Value.TagsRu!["Dog"]);
        Assert.Equal("пляж", result.Value.TagsRu["beach"]);
    }

    [Fact]
    public void Parse2_BlankOrMissingForms_AreNotAFailure()
    {
        const string Raw = """
            {"title":"T","title_ru":" ","description":"D","summary":"S",
             "tags":[{"en":"dog","ru":""},"beach",{"EN":"sand","RU":"песок"},{"en":"dog","ru":"пёс"},{"en":" ","ru":"пусто"}]}
            """;

        var result = ImageDescriptionContract.Parse(Raw, 2);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.TitleRu);
        Assert.Equal(["dog", "beach", "sand"], result.Value.Tags);
        Assert.Equal(new Dictionary<string, string> { ["sand"] = "песок", ["dog"] = "пёс" }, result.Value.TagsRu);
    }

    [Fact]
    public void Parse2_NoFormsAtAll_GivesNone()
    {
        var result = ImageDescriptionContract.Parse(ValidJson, 2);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.TitleRu);
        Assert.Null(result.Value.TagsRu);
        Assert.Equal(["dog", "beach"], result.Value.Tags);
    }

    [Theory]
    [InlineData("{\"title\":\"T\",\"description\":\"D\",\"summary\":\"S\",\"tags\":\"dog\"}", "en/ru pairs")]
    [InlineData("{\"title\":\"T\",\"description\":\"D\",\"summary\":\"S\",\"tags\":[{\"en\":[\"dog\"]}]}", "en/ru pairs")]
    [InlineData("{\"title\":\"T\",\"description\":\"D\",\"summary\":\"S\",\"tags\":[1]}", "en/ru pairs")]
    [InlineData("{\"title\":\"T\",\"title_ru\":[\"T\"],\"description\":\"D\",\"summary\":\"S\"}", "title_ru")]
    [InlineData("{\"title\":\"T\",\"title_ru\":\"Т\",\"description\":\"D\"}", "summary")]
    public void Parse2_UnusableAnswer_FailsWithReason(string raw, string reason)
    {
        var result = ImageDescriptionContract.Parse(raw, 2);

        Assert.False(result.IsSuccess);
        Assert.Contains(reason, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnknownSchema_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageDescriptionContract.Parse(ValidJson, 3));
}
