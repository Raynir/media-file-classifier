namespace MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

/// <summary>
/// Implemented by a date range filter (E10-S02-T01) to tell the filter panel which presets it offers. A preset's ID goes
/// into <see cref="DateRangeValue.Preset"/> and is resolved when the search runs, so saved searches stay relative.
/// </summary>
public interface IFilterPresets
{
    /// <summary>
    /// Gets the presets, in the order the panel shows them.
    /// </summary>
    IReadOnlyList<FilterPreset> Presets { get; }
}

/// <summary>
/// A named range a date filter resolves, e.g. the last seven days.
/// </summary>
/// <param name="Id">The ID stored in the filter's value, e.g. <c>last-7-days</c>; never changes.</param>
/// <param name="DisplayNameKey">The resource key of its name.</param>
public sealed record FilterPreset(string Id, string DisplayNameKey);
