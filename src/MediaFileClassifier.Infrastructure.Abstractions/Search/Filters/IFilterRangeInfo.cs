namespace MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

/// <summary>
/// Implemented by a numeric range filter (E10-S02-T01) to describe its number box in the filter panel. A filter that
/// doesn't implement it gets plain unitless boxes.
/// </summary>
public interface IFilterRangeInfo
{
    /// <summary>
    /// Gets the resource key of the unit shown beside the boxes (e.g. megapixels), or <see langword="null"/> for none.
    /// </summary>
    string? UnitKey { get; }

    /// <summary>
    /// Gets the lowest value the box accepts, or <see langword="null"/> for no lower bound.
    /// </summary>
    double? Minimum { get; }

    /// <summary>
    /// Gets the highest value the box accepts, or <see langword="null"/> for no upper bound.
    /// </summary>
    double? Maximum { get; }
}
