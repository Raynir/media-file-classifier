using System.Globalization;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// The media type filter (E10-S03): image, animated image, video. Values are the <see cref="MediaType"/> names; unknown
/// names are ignored, and a value with none known matches nothing.
/// </summary>
internal sealed class MediaTypeFilter : FilterDefinition<MultiSelectValue>, IFilterOptions
{
    /// <summary>
    /// The filter's ID.
    /// </summary>
    public const string FilterId = "media-type";

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_MediaType";

    /// <inheritdoc />
    public override int Order => 100;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.MultiSelect;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FilterOption>> GetOptionsAsync(string? prefix, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<FilterOption>>(
            [.. Enum.GetValues<MediaType>().Select(type => new FilterOption(type.ToString(), "Filter_MediaType_" + type))]);

    /// <inheritdoc />
    protected override string? ToSql(MultiSelectValue value, IFilterSql sql)
    {
        var types = value.Values
            .Select(name => Enum.TryParse<MediaType>(name, ignoreCase: true, out var type) && Enum.IsDefined(type) ? (int?)type : null)
            .OfType<int>()
            .Distinct()
            .Select(type => type.ToString(CultureInfo.InvariantCulture))
            .ToList();

        return types.Count == 0 ? "0" : $"{sql.MediaItem("media_type")} IN ({string.Join(", ", types)})";
    }
}
