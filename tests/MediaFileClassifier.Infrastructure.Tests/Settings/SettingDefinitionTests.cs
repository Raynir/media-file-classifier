using System.Text.Json;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Infrastructure.Tests.Settings;

/// <summary>
/// Tests for <see cref="SettingDefinition"/> and its value types (E17-S01-T01).
/// </summary>
public sealed class SettingDefinitionTests
{
    private static readonly ChoiceSetting<DayOfWeek> Day = new("test.day", SettingScope.App, SettingCategories.General, "Setting_Day", DayOfWeek.Monday);
    private static readonly IntegerSetting Count = new("test.count", SettingScope.App, SettingCategories.General, "Setting_Count", 2, minimum: 0, maximum: 5);

    [Fact]
    public void Choice_IsStoredAsCamelCaseName()
    {
        var json = Day.Serialize(DayOfWeek.Saturday);

        Assert.Equal("saturday", json.GetString());
        Assert.True(Day.TryDeserialize(json, out var value));
        Assert.Equal(DayOfWeek.Saturday, value);
    }

    [Theory]
    [InlineData("\"3\"")]
    [InlineData("\"monday, friday\"")]
    [InlineData("\"someday\"")]
    [InlineData("3")]
    [InlineData("null")]
    public void Choice_RejectsStoredValuesThatAreNotMemberNames(string json)
    {
        Assert.False(Day.TryDeserialize(Parse(json), out _));
    }

    [Fact]
    public void Choice_ListsOptionsWithLabelKeys()
    {
        IChoiceSetting choice = Day;

        Assert.Equal(7, choice.Options.Count);
        Assert.Equal("Setting_Day_Friday", choice.OptionLabelKey(DayOfWeek.Friday));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void Integer_ChecksTheRange(int value, bool valid)
    {
        var error = Count.Validate(value);

        Assert.Equal(valid, error is null);
        Assert.Equal(valid, Count.TryDeserialize(Count.Serialize(value), out _));
        if (error is not null)
        {
            Assert.Equal("SettingError_Range", error.MessageKey);
            Assert.Equal([0, 5], error.Arguments);
        }
    }

    [Fact]
    public void Integer_RejectsADefaultOutsideTheRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IntegerSetting("test.bad", SettingScope.App, SettingCategories.General, "Setting_Bad", 9, minimum: 0, maximum: 5));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("http://localhost:1234/v1", true)]
    [InlineData("https://example.com", true)]
    [InlineData("localhost:1234", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("not a url", false)]
    public void Text_EmptyOrHttpUrl(string value, bool valid)
    {
        Assert.Equal(valid, KnownSettings.OpenAiCompatibleEndpoint.Validate(value) is null);
    }

    [Fact]
    public void Boolean_RoundTrips()
    {
        Assert.True(KnownSettings.AnalyzeAutomatically.TryDeserialize(KnownSettings.AnalyzeAutomatically.Serialize(false), out var value));
        Assert.Equal(false, value);
        Assert.False(KnownSettings.AnalyzeAutomatically.TryDeserialize(Parse("\"false\""), out _));
    }

    [Fact]
    public void ValidateValue_RejectsAValueOfTheWrongType()
    {
        Assert.Throws<ArgumentException>(() => Count.ValidateValue("2"));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
