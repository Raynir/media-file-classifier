using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Ui.Tests.Search.Filters;

/// <summary>
/// Filters only the filter panel tests register: one per value kind, so the panel is shown to build its controls from
/// the kind alone (E10-S02-T01).
/// </summary>
internal static class TestFilters
{
    public static FilterDefinition<MultiSelectValue> Kinds { get; } = new TestMultiSelect();

    public static FilterDefinition<DateRangeValue> Dates { get; } = new TestDateRange();

    public static FilterDefinition<RangeValue> Megapixels { get; } = new TestRange();

    public static FilterDefinition<IncludeExcludeValue> Tags { get; } = new TestTags();

    public static FilterDefinition<MultiSelectValue> MediaType { get; } = new TestMediaType();

    public static TestBoolean Favorites { get; } = new("favorite", 500);

    public static TestBoolean Flag(string id, int order = 600) => new(id, order);

    internal sealed class TestBoolean(string id, int order) : FilterDefinition<BooleanValue>
    {
        private bool isAvailable = true;

        public override string Id => id;

        public override string DisplayNameKey => "Filter_Test_" + id;

        public override int Order => order;

        public override FilterValueKind Kind => FilterValueKind.Boolean;

        public override bool IsAvailable => isAvailable;

        public void SetAvailable(bool value)
        {
            isAvailable = value;
            RaiseAvailabilityChanged();
        }

        protected override string? ToSql(BooleanValue value, IFilterSql sql) => "1";
    }

    private sealed class TestMultiSelect : FilterDefinition<MultiSelectValue>, IFilterOptions
    {
        public override string Id => "test-kind";

        public override string DisplayNameKey => "Filter_MediaType";

        public override int Order => 100;

        public override FilterValueKind Kind => FilterValueKind.MultiSelect;

        public ValueTask<IReadOnlyList<FilterOption>> GetOptionsAsync(string? prefix, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<FilterOption>>(
                [new FilterOption("jpeg", DisplayName: "JPEG"), new FilterOption("Video", "Filter_MediaType_Video"), new FilterOption("png")]);

        protected override string? ToSql(MultiSelectValue value, IFilterSql sql) => "1";
    }

    private sealed class TestMediaType : FilterDefinition<MultiSelectValue>
    {
        public override string Id => "media-type";

        public override string DisplayNameKey => "Filter_MediaType";

        public override int Order => 100;

        public override FilterValueKind Kind => FilterValueKind.MultiSelect;

        protected override string? ToSql(MultiSelectValue value, IFilterSql sql) => "1";
    }

    private sealed class TestDateRange : FilterDefinition<DateRangeValue>, IFilterPresets
    {
        public override string Id => "test-date";

        public override string DisplayNameKey => "Filter_Test_date";

        public override int Order => 300;

        public override FilterValueKind Kind => FilterValueKind.DateRange;

        public IReadOnlyList<FilterPreset> Presets { get; } = [new("last-7-days", "Filter_Test_last7"), new("this-year", "Filter_Test_thisyear")];

        protected override string? ToSql(DateRangeValue value, IFilterSql sql) => "1";
    }

    private sealed class TestRange : FilterDefinition<RangeValue>, IFilterRangeInfo
    {
        public override string Id => "test-range";

        public override string DisplayNameKey => "Filter_Test_range";

        public override int Order => 400;

        public override FilterValueKind Kind => FilterValueKind.Range;

        public string? UnitKey => "Filter_Test_mp";

        public double? Minimum => 0;

        public double? Maximum => 1000;

        protected override string? ToSql(RangeValue value, IFilterSql sql) => "1";
    }

    private sealed class TestTags : FilterDefinition<IncludeExcludeValue>, IFilterOptions
    {
        public override string Id => "test-tags";

        public override string DisplayNameKey => "Filter_Test_tags";

        public override int Order => 700;

        public override FilterValueKind Kind => FilterValueKind.IncludeExclude;

        public ValueTask<IReadOnlyList<FilterOption>> GetOptionsAsync(string? prefix, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<FilterOption>>(
                [.. new[] { "cat", "car", "dog", "cart" }.Where(tag => string.IsNullOrEmpty(prefix) || tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(tag => new FilterOption(tag))]);

        protected override string? ToSql(IncludeExcludeValue value, IFilterSql sql) => "1";
    }
}
