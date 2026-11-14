namespace MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

/// <summary>
/// The kinds of filter (E10-S01): each one has its value type and its control in the filter panel (E10-S02).
/// </summary>
public enum FilterValueKind
{
    /// <summary>
    /// Pick any number of values; an item matches any of them (<see cref="MultiSelectValue"/>).
    /// </summary>
    MultiSelect,

    /// <summary>
    /// A numeric range, either end open (<see cref="RangeValue"/>).
    /// </summary>
    Range,

    /// <summary>
    /// A date range, as a preset or a custom range (<see cref="DateRangeValue"/>).
    /// </summary>
    DateRange,

    /// <summary>
    /// On or off (<see cref="BooleanValue"/>).
    /// </summary>
    Boolean,

    /// <summary>
    /// Values to include, all or any of them, and values to exclude (<see cref="IncludeExcludeValue"/>).
    /// </summary>
    IncludeExclude,

    /// <summary>
    /// A resolution preset, or a minimum displayed width and height (<see cref="ResolutionValue"/>).
    /// </summary>
    Resolution,

    /// <summary>
    /// Indexed folders and subfolders, with or without what's inside their subfolders (<see cref="FolderValue"/>).
    /// </summary>
    Folder,
}
