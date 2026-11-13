using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.UseCases.Search.Filters;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Search.Filters;

/// <summary>
/// Tests for <see cref="FilterState"/> and <see cref="FilterStateSerializer"/> (E10-S01-T02): a filter state survives
/// a round trip through JSON unchanged.
/// </summary>
public sealed class FilterStateTests
{
    private static readonly FilterRegistry Registry = new(TestFilters.All);

    private static FilterStateSerializer Serializer { get; } = new(Registry, NullLogger<FilterStateSerializer>.Instance);

    private static FilterState Full { get; } = FilterState.Empty
        .Set("type", new MultiSelectValue(["Video", "Image"]))
        .Set("size", new RangeValue(1.5, null))
        .Set("date", new DateRangeValue(null, new DateTimeOffset(2024, 3, 10, 12, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)))
        .Set("favorite", new BooleanValue(false))
        .Set("tags", new IncludeExcludeValue(["beach", "Пляж"], ["work"], matchAll: true));

    [Fact]
    public void EveryKindOfValue_SurvivesARoundTrip()
    {
        var json = FilterStateSerializer.Serialize(Full);

        var read = Serializer.Deserialize(json);

        Assert.Equal(Full, read);
        Assert.Equal(json, FilterStateSerializer.Serialize(read));
    }

    [Fact]
    public void ADatePreset_SurvivesARoundTrip()
    {
        var state = FilterState.Empty.Set("date", new DateRangeValue("last-7-days", null, null));

        Assert.Equal(state, Serializer.Deserialize(FilterStateSerializer.Serialize(state)));
    }

    [Fact]
    public void TheEmptyState_SurvivesARoundTrip()
    {
        var json = FilterStateSerializer.Serialize(FilterState.Empty);

        Assert.Equal("""{"version":1,"filters":[]}""", json);
        Assert.True(Serializer.Deserialize(json).IsEmpty);
    }

    [Fact]
    public void TheFormat_IsTheDocumentedOne() =>
        Assert.Equal(
            """{"version":1,"filters":[{"id":"type","value":{"values":["Video"]}}]}""",
            FilterStateSerializer.Serialize(FilterState.Empty.Set("type", new MultiSelectValue(["Video"]))));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"version":1}""")]
    public void UnreadableJson_GivesNoFilter(string? json) => Assert.True(Serializer.Deserialize(json).IsEmpty);

    [Fact]
    public void UnknownFiltersAndUnreadableValues_AreSkipped_TheRestIsKept()
    {
        var json = """
            {"version":1,"filters":[
              {"id":"gone","value":{"values":["x"]}},
              {"id":"size","value":"not a range"},
              {"value":{"value":true}},
              {"id":"favorite","value":{"value":true}}]}
            """;

        Assert.Equal(FilterState.Empty.Set("favorite", new BooleanValue(true)), Serializer.Deserialize(json));
    }

    [Fact]
    public void ANewerFormatVersion_GivesNoFilter() =>
        Assert.True(Serializer.Deserialize("""{"version":2,"filters":[{"id":"favorite","value":{"value":true}}]}""").IsEmpty);

    [Fact]
    public void AValueMissingAProperty_IsSkipped_NotReadWithADefault() =>
        Assert.True(Serializer.Deserialize("""{"version":1,"filters":[{"id":"favorite","value":{}},{"id":"size","value":{"min":1}}]}""").IsEmpty);

    [Fact]
    public void Values_CompareByContent_InAnyOrder()
    {
        Assert.Equal(new MultiSelectValue(["b", "a", "a"]), new MultiSelectValue(["a", "b"]));
        Assert.Equal(new MultiSelectValue(["b", "a"]).GetHashCode(), new MultiSelectValue(["a", "b"]).GetHashCode());
        Assert.Equal(new IncludeExcludeValue(["b", "a"], ["c"], true), new IncludeExcludeValue(["a", "b"], ["c"], true));
        Assert.NotEqual(new IncludeExcludeValue(["a"], [], true), new IncludeExcludeValue(["a"], [], false));
        Assert.NotEqual(Full, Full.Set("favorite", new BooleanValue(true)));
    }

    [Fact]
    public void Set_EmptyOrNullValue_RemovesTheFilter()
    {
        var state = FilterState.Empty.Set("type", new MultiSelectValue(["Video"]));

        Assert.True(state.Set("type", new MultiSelectValue([" "])).IsEmpty);
        Assert.True(state.Set("type", null).IsEmpty);
        Assert.True(FilterState.Empty.Set("size", new RangeValue(null, null)).IsEmpty);
        Assert.Same(state, state.Remove("other"));
    }

    [Fact]
    public void ToConditions_KeepsAvailableRegisteredFilters_InPanelOrder()
    {
        var hidden = new TestFilter<BooleanValue>("hidden", FilterValueKind.Boolean);
        hidden.SetAvailable(false);
        var registry = new FilterRegistry([.. TestFilters.All, hidden]);
        var state = Full.Set("hidden", new BooleanValue(true)).Set("gone", new BooleanValue(true)).Set("type", new BooleanValue(true));

        var conditions = state.ToConditions(registry);

        Assert.Equal(["size", "date", "favorite", "tags"], conditions.Select(condition => condition.Definition.Id));
    }
}
