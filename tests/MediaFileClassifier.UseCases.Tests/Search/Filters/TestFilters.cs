using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.UseCases.Tests.Search.Filters;

/// <summary>
/// A filter of any value type, with SQL that names its ID and value, so tests can see what reached the search.
/// </summary>
/// <typeparam name="TValue">The value type.</typeparam>
/// <param name="id">The filter's ID.</param>
/// <param name="kind">Its kind.</param>
/// <param name="order">Its panel order.</param>
internal sealed class TestFilter<TValue>(string id, FilterValueKind kind, int order = 0) : FilterDefinition<TValue>
    where TValue : FilterValue
{
    private bool available = true;

    public override string Id => id;

    public override string DisplayNameKey => "Filter_" + id;

    public override int Order => order;

    public override FilterValueKind Kind => kind;

    public override bool IsAvailable => available;

    public void SetAvailable(bool value)
    {
        available = value;
        RaiseAvailabilityChanged();
    }

    protected override string? ToSql(TValue value, IFilterSql sql) => $"{id} = {sql.Parameter(value.ToString())}";
}

/// <summary>
/// One filter of each kind.
/// </summary>
internal static class TestFilters
{
    public static TestFilter<MultiSelectValue> Type { get; } = new("type", FilterValueKind.MultiSelect, 100);

    public static TestFilter<RangeValue> Size { get; } = new("size", FilterValueKind.Range, 200);

    public static TestFilter<DateRangeValue> Date { get; } = new("date", FilterValueKind.DateRange, 300);

    public static TestFilter<BooleanValue> Favorite { get; } = new("favorite", FilterValueKind.Boolean, 400);

    public static TestFilter<IncludeExcludeValue> Tags { get; } = new("tags", FilterValueKind.IncludeExclude, 500);

    public static IReadOnlyList<IFilterDefinition> All { get; } = [Type, Size, Date, Favorite, Tags];
}
