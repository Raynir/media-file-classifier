using System.Collections.Immutable;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.UseCases.Search.Filters;

/// <summary>
/// The active filters (E10-S01-T02): each filter's ID with its value. Immutable; a filter whose value is empty isn't
/// stored. Two states are equal when they hold the same filters with equal values.
/// </summary>
public sealed class FilterState : IEquatable<FilterState>
{
    private readonly ImmutableSortedDictionary<string, FilterValue> values;

    private FilterState(ImmutableSortedDictionary<string, FilterValue> values) => this.values = values;

    /// <summary>
    /// Gets the state with no filter.
    /// </summary>
    public static FilterState Empty { get; } = new(ImmutableSortedDictionary.Create<string, FilterValue>(StringComparer.Ordinal));

    /// <summary>
    /// Gets a value indicating whether no filter is set.
    /// </summary>
    public bool IsEmpty => values.IsEmpty;

    /// <summary>
    /// Gets the filters set, by ID.
    /// </summary>
    public IReadOnlyDictionary<string, FilterValue> Values => values;

    /// <summary>
    /// Gets a filter's value.
    /// </summary>
    /// <param name="id">The filter's ID.</param>
    /// <returns>Its value, or <see langword="null"/> if it isn't set.</returns>
    public FilterValue? Get(string id) => values.GetValueOrDefault(id);

    /// <summary>
    /// Sets a filter's value; an empty or <see langword="null"/> value removes the filter.
    /// </summary>
    /// <param name="id">The filter's ID.</param>
    /// <param name="value">The value.</param>
    /// <returns>The new state.</returns>
    public FilterState Set(string id, FilterValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return value is null || value.IsEmpty ? Remove(id) : new FilterState(values.SetItem(id, value));
    }

    /// <summary>
    /// Removes a filter.
    /// </summary>
    /// <param name="id">The filter's ID.</param>
    /// <returns>The new state.</returns>
    public FilterState Remove(string id) => values.ContainsKey(id) ? new FilterState(values.Remove(id)) : this;

    /// <summary>
    /// Builds the search conditions of the filters registered and available now, in panel order. Unknown and
    /// unavailable filters, and values of the wrong type, are left out.
    /// </summary>
    /// <param name="registry">The registered filters.</param>
    /// <returns>The conditions.</returns>
    public IReadOnlyList<FilterCondition> ToConditions(FilterRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return [.. registry.All
            .Where(filter => filter.IsAvailable && values.TryGetValue(filter.Id, out var value) && filter.ValueType.IsInstanceOfType(value))
            .Select(filter => new FilterCondition(filter, values[filter.Id]))];
    }

    /// <summary>
    /// Adds these filters' conditions to a search filter (E10-S01-T03).
    /// </summary>
    /// <param name="filter">The search filter, e.g. the browse scope.</param>
    /// <param name="registry">The registered filters.</param>
    /// <returns>The search filter with the conditions added, or <paramref name="filter"/> if there are none.</returns>
    public SearchFilter ApplyTo(SearchFilter filter, FilterRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var conditions = ToConditions(registry);
        return conditions.Count == 0 ? filter : filter with { Conditions = [.. filter.Conditions ?? [], .. conditions] };
    }

    /// <inheritdoc />
    public bool Equals(FilterState? other) =>
        other is not null && values.Count == other.values.Count && values.All(pair => other.values.TryGetValue(pair.Key, out var value) && pair.Value.Equals(value));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FilterState);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var (id, value) in values)
        {
            hash.Add(id, StringComparer.Ordinal);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
