using System.Globalization;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// The processing status filter (E10-S03): pending (discovered or queued), indexed, error and missing items (E01-S02-T03).
/// Excluded items never reach the search results, so they aren't offered. Unknown values are ignored, and a value with
/// none known matches nothing.
/// </summary>
internal sealed class StatusFilter : FilterDefinition<MultiSelectValue>, IFilterOptions
{
    /// <summary>
    /// The filter's ID.
    /// </summary>
    public const string FilterId = "status";

    /// <summary>
    /// Items not processed yet: discovered or queued.
    /// </summary>
    public const string Pending = "pending";

    /// <summary>
    /// Processed, searchable items.
    /// </summary>
    public const string Indexed = "indexed";

    /// <summary>
    /// Items whose processing failed.
    /// </summary>
    public const string Error = "error";

    /// <summary>
    /// Items whose file is no longer at its path.
    /// </summary>
    public const string Missing = "missing";

    private static readonly (string Value, string Key, MediaItemStatus[] Statuses)[] Values =
    [
        (Pending, "Filter_Status_Pending", [MediaItemStatus.Discovered, MediaItemStatus.Pending]),
        (Indexed, "Filter_Status_Indexed", [MediaItemStatus.Indexed]),
        (Error, "Filter_Status_Error", [MediaItemStatus.Error]),
        (Missing, "Filter_Status_Missing", [MediaItemStatus.Missing]),
    ];

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_Status";

    /// <inheritdoc />
    public override int Order => 500;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.MultiSelect;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FilterOption>> GetOptionsAsync(string? prefix, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<FilterOption>>(
            [.. Values
                .Select(value => new FilterOption(value.Value, value.Key))]);

    /// <inheritdoc />
    protected override string? ToSql(MultiSelectValue value, IFilterSql sql)
    {
        var statuses = Values
            .Where(known => value.Values.Contains(known.Value, StringComparer.OrdinalIgnoreCase))
            .SelectMany(known => known.Statuses)
            .Select(status => ((int)status).ToString(CultureInfo.InvariantCulture))
            .ToList();

        return statuses.Count == 0 ? "0" : $"{sql.MediaItem("status")} IN ({string.Join(", ", statuses)})";
    }
}
