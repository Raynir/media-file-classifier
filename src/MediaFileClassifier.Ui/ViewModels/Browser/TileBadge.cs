using Avalonia;
using Avalonia.Media;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// A status badge in a tile's badge row (E14-S01-T04). The tile draws every badge with one template, styled by
/// <see cref="Kind"/> as a <c>StatusBadge</c> class, so a new badge type (Favorite in E11-S01, Offline and Missing in
/// E14-S07) only adds a case to <see cref="For"/>.
/// </summary>
/// <param name="Kind">The <c>StatusBadge</c> style class, e.g. <c>Pending</c>.</param>
/// <param name="Icon">The icon.</param>
/// <param name="Label">The accessible name.</param>
/// <param name="ToolTip">More detail, e.g. the error message.</param>
public sealed record TileBadge(string Kind, Geometry? Icon, string Label, string ToolTip)
{
    // Tiles ask for badges on every scroll step; resource lookups are the expensive part.
    private static readonly Dictionary<string, Geometry?> Icons = [];

    /// <summary>
    /// Gets the badges of an item, in the order the tile shows them.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The badges; empty for most items.</returns>
    public static IReadOnlyList<TileBadge> For(MediaItemSummary item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Status switch
        {
            MediaItemStatus.Discovered or MediaItemStatus.Pending => [Create("Pending", "Icon.Status.Pending", "Tile_Badge_Pending", null)],
            MediaItemStatus.Error => [Create("Error", "Icon.Status.Error", "Tile_Badge_Error", item.StatusReason)],
            _ => [],
        };
    }

    private static TileBadge Create(string kind, string iconKey, string labelKey, string? detail)
    {
        var label = Localizer.Current[labelKey];
        if (!Icons.TryGetValue(iconKey, out var icon))
        {
            icon = Application.Current?.TryGetResource(iconKey, null, out var resource) == true ? resource as Geometry : null;
            Icons[iconKey] = icon;
        }

        return new TileBadge(kind, icon, label, string.IsNullOrWhiteSpace(detail) ? label : $"{label}: {detail}");
    }
}
