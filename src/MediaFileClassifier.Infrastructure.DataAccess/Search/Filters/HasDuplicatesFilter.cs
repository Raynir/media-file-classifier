using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// "Has duplicates" (E12-S05-T02): the items that are members of an Open duplicate group, of any type. The value off
/// filters nothing, so the filter is a plain on/off checkbox in the filter panel. The lookup uses the member table's
/// item index, so it stays fast on a large library.
/// </summary>
internal sealed class HasDuplicatesFilter : FilterDefinition<BooleanValue>
{
    /// <summary>The filter ID.</summary>
    public const string FilterId = "has-duplicates";

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_HasDuplicates";

    /// <inheritdoc />
    public override int Order => 540;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.Boolean;

    /// <inheritdoc />
    protected override string? ToSql(BooleanValue value, IFilterSql sql) =>
        value.Value
            ? $"""EXISTS (SELECT 1 FROM "duplicate_group_member" m JOIN "duplicate_group" g ON g."id" = m."group_id" WHERE m."item_id" = {sql.ItemId} AND g."state" = 0)"""
            : null;
}
