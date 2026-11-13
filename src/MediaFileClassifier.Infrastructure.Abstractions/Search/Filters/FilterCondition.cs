namespace MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

/// <summary>
/// One filter with its value, ready for the search (<see cref="SearchFilter.Conditions"/>).
/// </summary>
/// <param name="Definition">The filter.</param>
/// <param name="Value">Its value, of the definition's <see cref="IFilterDefinition.ValueType"/>.</param>
public sealed record FilterCondition(IFilterDefinition Definition, FilterValue Value)
{
    /// <summary>
    /// Builds the SQL condition.
    /// </summary>
    /// <param name="sql">The SQL builder.</param>
    /// <returns>The condition, or <see langword="null"/> if it filters nothing.</returns>
    public string? ToSql(IFilterSql sql) => Definition.ToSql(Value, sql);
}
