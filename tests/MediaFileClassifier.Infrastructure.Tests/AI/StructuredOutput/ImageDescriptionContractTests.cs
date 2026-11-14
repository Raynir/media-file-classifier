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

    [Fact]
    public void Schema_LimitsEveryTextAndList_SoAGrammarStopsRunawayOutput()
    {
        var properties = ImageDescriptionContract.Schema.GetProperty("properties");

        Assert.Equal(ImageDescriptionContract.MaxTitleLength, properties.GetProperty("title").GetProperty("maxLength").GetInt32());
        Assert.Equal(ImageDescriptionContract.MaxDescriptionLength, properties.GetProperty("description").GetProperty("maxLength").GetInt32());
        Assert.Equal(ImageDescriptionContract.MaxSummaryLength, properties.GetProperty("summary").GetProperty("maxLength").GetInt32());
        foreach (var list in new[] { "tags", "objects", "scenes", "actions", "emotions" })
        {
            Assert.Equal(ImageDescriptionContract.MaxListItems, properties.GetProperty(list).GetProperty("maxItems").GetInt32());
            Assert.Equal(ImageDescriptionContract.MaxListItemLength, properties.GetProperty(list).GetProperty("items").GetProperty("maxLength").GetInt32());
        }
    }

    [Fact]
    public void Schema_RequiresEveryFieldAndAllowsNoOthers()
    {
        var schema = ImageDescriptionContract.Schema;

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

        Assert.Equal(ImageDescriptionContract.MaxListItems, result.Value!.Tags.Count);
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

    [Fact]
    public void Schema_IsValidJson()
    {
        Assert.Equal(JsonValueKind.Object, ImageDescriptionContract.Schema.ValueKind);
    }
}
