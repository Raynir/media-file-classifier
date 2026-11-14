using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// The resolution filter (E10-S03): a preset, or a minimum width and height, on the dimensions as displayed
/// (E03-S03-T05), so a portrait photo stored landscape with an EXIF rotation counts as portrait. Presets compare the
/// short and the long side, so they keep portrait and landscape items alike. Items without dimensions yet never match,
/// and an unknown preset matches nothing.
/// </summary>
internal sealed class ResolutionFilter : FilterDefinition<ResolutionValue>, IFilterOptions
{
    /// <summary>
    /// The filter's ID.
    /// </summary>
    public const string FilterId = "resolution";

    /// <summary>
    /// The preset of small items: the long side is under 800 pixels.
    /// </summary>
    public const string Small = "small";

    /// <summary>
    /// The preset of HD or better: at least 1280 × 720.
    /// </summary>
    public const string Hd = "hd";

    /// <summary>
    /// The preset of Full HD or better: at least 1920 × 1080.
    /// </summary>
    public const string FullHd = "full-hd";

    /// <summary>
    /// The preset of 4K or better: at least 3840 × 2160.
    /// </summary>
    public const string UltraHd = "4k";

    private static readonly (string Preset, string Key)[] Presets =
    [
        (Small, "Filter_Resolution_Small"),
        (Hd, "Filter_Resolution_Hd"),
        (FullHd, "Filter_Resolution_FullHd"),
        (UltraHd, "Filter_Resolution_4K"),
    ];

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_Resolution";

    /// <inheritdoc />
    public override int Order => 400;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.Resolution;

    /// <summary>
    /// Lists the presets, in the order the panel shows them.
    /// </summary>
    /// <param name="prefix">Ignored: the list is fixed, and the panel narrows it by the text it shows.</param>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>The presets: their IDs and display-name keys.</returns>
    public ValueTask<IReadOnlyList<FilterOption>> GetOptionsAsync(string? prefix, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<FilterOption>>(
            [.. Presets
                .Select(preset => new FilterOption(preset.Preset, preset.Key))]);

    /// <inheritdoc />
    protected override string? ToSql(ResolutionValue value, IFilterSql sql)
    {
        var width = sql.TechnicalMetadata("width");
        var height = sql.TechnicalMetadata("height");

        if (!string.IsNullOrWhiteSpace(value.Preset))
        {
            // min() and max() with several arguments are SQLite's scalar functions, null if either side is null.
            var shortSide = $"min({width}, {height})";
            var longSide = $"max({width}, {height})";
            return value.Preset switch
            {
                Small => $"{longSide} < {sql.Parameter(800)}",
                Hd => AtLeast(720, 1280),
                FullHd => AtLeast(1080, 1920),
                UltraHd => AtLeast(2160, 3840),
                _ => "0",
            };

            string AtLeast(int shortest, int longest) => $"{shortSide} >= {sql.Parameter(shortest)} AND {longSide} >= {sql.Parameter(longest)}";
        }

        // Orientations from Transpose (5) on swap the axes (EXIF Orientation; D106 for videos).
        var swaps = $"{sql.TechnicalMetadata("orientation")} >= 5";
        var conditions = new List<string>();
        if (value.MinWidth is { } minWidth)
        {
            conditions.Add($"(CASE WHEN {swaps} THEN {height} ELSE {width} END) >= {sql.Parameter(minWidth)}");
        }

        if (value.MinHeight is { } minHeight)
        {
            conditions.Add($"(CASE WHEN {swaps} THEN {width} ELSE {height} END) >= {sql.Parameter(minHeight)}");
        }

        return string.Join(" AND ", conditions);
    }
}
