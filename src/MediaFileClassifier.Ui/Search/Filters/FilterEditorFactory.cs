using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// Builds the control for a filter from its <see cref="FilterValueKind"/> (E10-S02-T01).
/// </summary>
public static class FilterEditorFactory
{
    /// <summary>
    /// Creates the control for a filter.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="localizer">UI strings.</param>
    /// <returns>The control.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The filter's kind has no control.</exception>
    public static FilterEditorViewModel Create(IFilterDefinition filter, Localizer localizer) => filter.Kind switch
    {
        FilterValueKind.MultiSelect => new MultiSelectFilterViewModel(filter, localizer),
        FilterValueKind.Range => new RangeFilterViewModel(filter, localizer),
        FilterValueKind.DateRange => new DateRangeFilterViewModel(filter, localizer),
        FilterValueKind.Boolean => new BooleanFilterViewModel(filter, localizer),
        FilterValueKind.IncludeExclude => new TagFilterViewModel(filter, localizer),
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter.Kind, $"Filter {filter.Id} has a kind without a control."),
    };
}
