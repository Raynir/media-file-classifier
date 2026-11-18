using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// "Favorites only" (E11-S01-T04): the items the user marked as favorites, or with the value off, the ones that are
/// not. It applies to every search mode through the filter model, so it combines with text search and the other
/// filters, and the Favorites chip above the results switches it.
/// </summary>
internal sealed class FavoriteFilter : FilterDefinition<BooleanValue>
{
    /// <summary>The filter ID. The filter bar's Favorites chip uses the same one.</summary>
    public const string FilterId = "favorite";

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_Favorite";

    /// <inheritdoc />
    public override int Order => 150;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.Boolean;

    /// <inheritdoc />
    protected override string? ToSql(BooleanValue value, IFilterSql sql) =>
        $"{sql.MediaItem("is_favorite")} = {sql.Parameter(value.Value)}";
}
