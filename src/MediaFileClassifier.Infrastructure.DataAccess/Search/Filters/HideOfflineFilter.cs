using System.Globalization;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// "Hide offline items" (E14-S07-T03): leaves out the items whose indexed folder is Offline (D025), in every search
/// mode. The value off filters nothing, so the filter is a plain on/off switch in the filter panel.
/// </summary>
internal sealed class HideOfflineFilter : FilterDefinition<BooleanValue>
{
    /// <summary>The filter ID.</summary>
    public const string FilterId = "hide-offline";

    private static readonly string Offline = ((int)FolderStatus.Offline).ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_HideOffline";

    /// <inheritdoc />
    public override int Order => 550;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.Boolean;

    /// <inheritdoc />
    protected override string? ToSql(BooleanValue value, IFilterSql sql) =>
        value.Value
            ? $"""{sql.MediaItem("folder_id")} NOT IN (SELECT f."id" FROM "indexed_folder" f WHERE f."status" = {Offline})"""
            : null;
}
