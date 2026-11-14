using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Search.Filters;

namespace MediaFileClassifier.Ui.Tests.Search.Filters;

/// <summary>
/// Tests for the filter panel's controls, one per value kind (E10-S02-T01).
/// </summary>
public sealed class FilterEditorTests
{
    private readonly Localizer localizer = TestLocalizer.EnUs();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(FilterValueKind.MultiSelect, typeof(MultiSelectFilterViewModel))]
    [InlineData(FilterValueKind.Range, typeof(RangeFilterViewModel))]
    [InlineData(FilterValueKind.DateRange, typeof(DateRangeFilterViewModel))]
    [InlineData(FilterValueKind.Boolean, typeof(BooleanFilterViewModel))]
    [InlineData(FilterValueKind.IncludeExclude, typeof(TagFilterViewModel))]
    public void Factory_BuildsTheControlOfTheKind(FilterValueKind kind, Type expected)
    {
        IFilterDefinition filter = kind switch
        {
            FilterValueKind.MultiSelect => TestFilters.Kinds,
            FilterValueKind.Range => TestFilters.Megapixels,
            FilterValueKind.DateRange => TestFilters.Dates,
            FilterValueKind.Boolean => TestFilters.Favorites,
            _ => TestFilters.Tags,
        };

        Assert.IsType(expected, FilterEditorFactory.Create(filter, localizer));
    }

    [Fact]
    public async Task MultiSelect_ShowsTheOptionsWithTheirNames_AndValueIsTheTickedOnes()
    {
        var editor = new MultiSelectFilterViewModel(TestFilters.Kinds, localizer);
        await editor.LoadOptionsAsync(Token);
        var changes = 0;
        editor.ValueChanged += (_, _) => changes++;

        editor.Options[1].IsChecked = true;
        editor.Options[0].IsChecked = true;

        Assert.Equal(["JPEG", "Video", "png"], editor.Options.Select(option => option.Text));
        Assert.Equal(new MultiSelectValue(["jpeg", "Video"]), editor.Value);
        Assert.Equal(2, changes);
        Assert.Equal("Video, JPEG", editor.Describe(editor.Value!));
    }

    [Fact]
    public async Task MultiSelect_LoadBeforeTheOptionsExist_TicksThemOnceTheyLoad_WithoutRaisingChanged()
    {
        var editor = new MultiSelectFilterViewModel(TestFilters.Kinds, localizer);
        var changes = 0;
        editor.ValueChanged += (_, _) => changes++;

        editor.Load(new MultiSelectValue(["png"]));
        await editor.LoadOptionsAsync(Token);

        Assert.Equal(new MultiSelectValue(["png"]), editor.Value);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Range_ParsesBothBoxes_AndTextThatIsNotANumberCountsAsEmpty()
    {
        var editor = new RangeFilterViewModel(TestFilters.Megapixels, localizer) { MinText = "2", MaxText = "abc" };

        Assert.Equal(new RangeValue(2, null), editor.Value);

        editor.MaxText = "5000";

        Assert.Equal(new RangeValue(2, 1000), editor.Value);
    }

    [Fact]
    public void Range_Empty_IsNoFilter_AndLoadFillsTheBoxes()
    {
        var editor = new RangeFilterViewModel(TestFilters.Megapixels, localizer);
        Assert.Null(editor.Value);

        editor.Load(new RangeValue(1.5, 12));

        Assert.Equal(1.5, double.Parse(editor.MinText, System.Globalization.CultureInfo.CurrentCulture));
        Assert.Equal(12, double.Parse(editor.MaxText, System.Globalization.CultureInfo.CurrentCulture));
        Assert.Equal(new RangeValue(1.5, 12), editor.Value);
    }

    [Fact]
    public void DateRange_PresetAndCustomRangeExcludeEachOther()
    {
        var editor = new DateRangeFilterViewModel(TestFilters.Dates, localizer);
        Assert.Equal(["Any time", "last-7-days", "this-year"], editor.Presets.Select(preset => preset.Text));

        editor.SelectedPreset = editor.Presets[1];
        Assert.Equal(new DateRangeValue("last-7-days", null, null), editor.Value);

        editor.FromDate = new DateTime(2026, 3, 1);
        Assert.Equal(editor.Presets[0], editor.SelectedPreset);
        Assert.Equal(new DateRangeValue(null, new DateTimeOffset(new DateTime(2026, 3, 1)), null), editor.Value);

        editor.SelectedPreset = editor.Presets[2];
        Assert.Null(editor.FromDate);
        Assert.Equal(new DateRangeValue("this-year", null, null), editor.Value);
    }

    [Fact]
    public void DateRange_CustomEndDayIsInclusive_SoTheValueEndsAtTheNextMidnight()
    {
        var editor = new DateRangeFilterViewModel(TestFilters.Dates, localizer) { ToDate = new DateTime(2026, 3, 31) };

        var value = Assert.IsType<DateRangeValue>(editor.Value);
        Assert.Equal(new DateTimeOffset(new DateTime(2026, 4, 1)), value.To);

        var other = new DateRangeFilterViewModel(TestFilters.Dates, localizer);
        other.Load(value);
        Assert.Equal(new DateTime(2026, 3, 31), other.ToDate);
    }

    [Fact]
    public void Range_MinAboveMax_IsSwapped()
    {
        var editor = new RangeFilterViewModel(TestFilters.Megapixels, localizer) { MinText = "9", MaxText = "3" };

        Assert.Equal(new RangeValue(3, 9), editor.Value);
    }

    [Fact]
    public void DateRange_FromAfterTo_IsSwapped()
    {
        var editor = new DateRangeFilterViewModel(TestFilters.Dates, localizer) { FromDate = new DateTime(2026, 5, 10), ToDate = new DateTime(2026, 5, 1) };

        Assert.Equal(new DateRangeValue(null, new DateTimeOffset(new DateTime(2026, 5, 1)), new DateTimeOffset(new DateTime(2026, 5, 11))), editor.Value);
    }

    [Fact]
    public void DateRange_WithPresetsOnlyInTheFilterOptions_ListsThem()
    {
        var editor = new DateRangeFilterViewModel(TestFilters.OptionDates, localizer);

        Assert.Equal(["Any time", "Today", "This year"], editor.Presets.Select(preset => preset.Text));
    }

    [Fact]
    public void Resolution_PresetWinsOverTheBoxes_AndTypingResetsThePreset()
    {
        var editor = new ResolutionFilterViewModel(TestFilters.Resolution, localizer);
        Assert.Equal(["Any size", "HD or better", "4K or better"], editor.Presets.Select(preset => preset.Text));

        editor.MinWidthText = "1920";
        Assert.Equal(new ResolutionValue(null, 1920, null), editor.Value);

        editor.SelectedPreset = editor.Presets[1];
        Assert.Equal(new ResolutionValue("hd", null, null), editor.Value);
        Assert.Equal(string.Empty, editor.MinWidthText);

        editor.MinHeightText = "1080";
        Assert.Equal(editor.Presets[0], editor.SelectedPreset);
        Assert.Equal(new ResolutionValue(null, null, 1080), editor.Value);
    }

    [Fact]
    public void Resolution_BoxesThatAreNotPositiveWholeNumbers_AreIgnored()
    {
        var editor = new ResolutionFilterViewModel(TestFilters.Resolution, localizer) { MinWidthText = "-4", MinHeightText = "abc" };

        Assert.Null(editor.Value);
    }

    [Fact]
    public void Factory_TryCreate_LeavesOutAKindWithoutAControl()
    {
        Assert.NotNull(FilterEditorFactory.TryCreate(TestFilters.Resolution, localizer));
        Assert.Null(FilterEditorFactory.TryCreate(new UnknownKindFilter(), localizer));
    }

    [Fact]
    public void Boolean_UntickedIsNoFilter()
    {
        var editor = new BooleanFilterViewModel(TestFilters.Favorites, localizer);
        Assert.Null(editor.Value);

        editor.IsOn = true;

        Assert.Equal(new BooleanValue(true), editor.Value);
    }

    [Fact]
    public void Tags_IncludeExcludeAndMatchAll_BuildTheValue_AndMovingATagSwitchesSides()
    {
        var editor = new TagFilterViewModel(TestFilters.Tags, localizer);

        editor.AddInclude("cat");
        editor.AddInclude("dog");
        editor.AddExclude("dog");
        editor.MatchAll = true;

        Assert.Equal(new IncludeExcludeValue(["cat"], ["dog"], matchAll: true), editor.Value);
        Assert.Equal("cat; not dog", editor.Describe(editor.Value!));
    }

    [Fact]
    public void Tags_MovingATagToTheOtherSide_IgnoresCase()
    {
        var editor = new TagFilterViewModel(TestFilters.Tags, localizer);
        editor.AddExclude("cat");

        editor.AddInclude("Cat");

        Assert.Equal(["Cat"], editor.Include);
        Assert.Empty(editor.Exclude);
    }

    [Fact]
    public void Tags_TypedTextSurvivesALoad()
    {
        var editor = new TagFilterViewModel(TestFilters.Tags, localizer) { Query = "bi" };

        editor.Load(new IncludeExcludeValue(["cat"], [], matchAll: false));

        Assert.Equal("bi", editor.Query);
    }

    [Fact]
    public void Tags_TypedTextIsAddedOnEnter_AndBlankTextIsIgnored()
    {
        var editor = new TagFilterViewModel(TestFilters.Tags, localizer) { Query = "  bird " };

        editor.AddIncludeCommand.Execute(null);
        editor.AddIncludeCommand.Execute(null);

        Assert.Equal(["bird"], editor.Include);
        Assert.Equal(string.Empty, editor.Query);
    }

    [Fact]
    public async Task Tags_SuggestWhatTheUserTypes_WithoutTheTagsAlreadyChosen()
    {
        var editor = new TagFilterViewModel(TestFilters.Tags, localizer);
        editor.AddInclude("car");

        editor.Query = "ca";
        await editor.UpdateSuggestionsAsync();

        Assert.Equal(["cat", "cart"], editor.Suggestions);
        Assert.True(editor.HasSuggestions);
    }

    [Fact]
    public void Clear_EmptiesTheControl_AndReportsIt()
    {
        var editor = new BooleanFilterViewModel(TestFilters.Favorites, localizer) { IsOn = true };
        var changes = 0;
        editor.ValueChanged += (_, _) => changes++;

        editor.Clear();

        Assert.Null(editor.Value);
        Assert.Equal(1, changes);
    }

    private sealed class UnknownKindFilter : FilterDefinition<BooleanValue>
    {
        public override string Id => "unknown";

        public override string DisplayNameKey => "Filter_Unknown";

        public override int Order => 1;

        public override FilterValueKind Kind => (FilterValueKind)99;

        protected override string? ToSql(BooleanValue value, IFilterSql sql) => "1";
    }
}
