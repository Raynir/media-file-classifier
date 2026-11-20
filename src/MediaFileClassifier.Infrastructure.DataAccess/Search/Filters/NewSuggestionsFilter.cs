using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// "Has new AI suggestions" (E06-S07-T06): a user-owned field with a differing new AI value, or a tag suggestion
/// (added or removed, E06-S07-T04).
/// </summary>
internal sealed class NewSuggestionsFilter : FilterDefinition<BooleanValue>
{
    public const string FilterId = "suggestions";

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_NewSuggestions";

    /// <inheritdoc />
    public override int Order => 530;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.Boolean;

    /// <inheritdoc />
    protected override string? ToSql(BooleanValue value, IFilterSql sql)
    {
        var has = $"""(EXISTS (SELECT 1 FROM "user_field_value" u WHERE u."item_id" = {sql.ItemId} AND u."suggestion_run_id" IS NOT NULL) OR EXISTS (SELECT 1 FROM "tag_suggestion" s WHERE s."item_id" = {sql.ItemId}))""";
        return value.Value ? has : $"NOT {has}";
    }
}
