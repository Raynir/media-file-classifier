using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.UseCases.Search.Filters;

/// <summary>
/// Every registered filter (E10-S01): the singleton <see cref="IFilterDefinition"/> services, in panel order. Adding a
/// filter is registering a definition (spec 1.11).
/// </summary>
public sealed class FilterRegistry
{
    private readonly Dictionary<string, IFilterDefinition> byId;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterRegistry"/> class.
    /// </summary>
    /// <param name="filters">The registered filters.</param>
    /// <exception cref="InvalidOperationException">Two filters have the same ID, or a filter's value type isn't a
    /// <see cref="FilterValue"/>.</exception>
    public FilterRegistry(IEnumerable<IFilterDefinition> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        byId = new Dictionary<string, IFilterDefinition>(StringComparer.Ordinal);
        foreach (var filter in filters.Distinct())
        {
            if (!typeof(FilterValue).IsAssignableFrom(filter.ValueType) || filter.ValueType.IsAbstract)
            {
                throw new InvalidOperationException($"Filter {filter.Id} has the value type {filter.ValueType.Name}, which isn't a FilterValue.");
            }

            if (!byId.TryAdd(filter.Id, filter))
            {
                throw new InvalidOperationException($"Two filters have the ID {filter.Id}.");
            }
        }

        All = [.. byId.Values.OrderBy(filter => filter.Order).ThenBy(filter => filter.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Gets every filter, in panel order, available or not.
    /// </summary>
    public IReadOnlyList<IFilterDefinition> All { get; }

    /// <summary>
    /// Gets the filters available now, in panel order: the ones the filter panel shows.
    /// </summary>
    public IReadOnlyList<IFilterDefinition> Available => [.. All.Where(filter => filter.IsAvailable)];

    /// <summary>
    /// Finds a filter.
    /// </summary>
    /// <param name="id">Its ID.</param>
    /// <returns>The filter, or <see langword="null"/> if none has that ID.</returns>
    public IFilterDefinition? Find(string id) => byId.GetValueOrDefault(id);
}
