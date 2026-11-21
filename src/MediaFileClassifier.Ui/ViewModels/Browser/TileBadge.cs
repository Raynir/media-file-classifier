using Avalonia;
using Avalonia.Media;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// A status badge in a tile's badge row (E14-S01-T04). The tile draws every badge with one template, styled by
/// <see cref="Kind"/> as a <c>StatusBadge</c> class, so a new badge type only adds a case to <see cref="For"/>. Offline
/// and Missing items (E14-S07, D025) get their own badge before the status badge.
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

        // An Offline folder hides whether its files are still there, so Offline wins over Missing (as in the details
        // panel). Either one comes first: it says why the file can't be used.
        var availability = Availability(item);
        var status = item.Status switch
        {
            MediaItemStatus.Discovered or MediaItemStatus.Pending => Create("Pending", "Icon.Status.Pending", "Tile_Badge_Pending", null),
            MediaItemStatus.Error => Create("Error", "Icon.Status.Error", "Tile_Badge_Error", item.StatusReason),
            _ => null,
        };
        return (availability, status) switch
        {
            (null, null) => [],
            (null, { } only) => [only],
            ({ } only, null) => [only],
            ({ } first, { } second) => [first, second],
        };
    }

    /// <summary>
    /// Gets the Offline or Missing badge of an item, or <see langword="null"/> when its file is available (E14-S07).
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The badge, or <see langword="null"/>.</returns>
    public static TileBadge? Availability(MediaItemSummary item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsFolderOffline)
        {
            return Create("Offline", "Icon.Status.Offline", "Tile_Badge_Offline", OfflineDetail(item));
        }

        return item.Status == MediaItemStatus.Missing
            ? Create("Missing", "Icon.Status.Missing", "Tile_Badge_Missing", Localizer.Current["Tile_Badge_MissingDetail"])
            : null;
    }

    /// <summary>
    /// Gets what an offline item's badge names: the drive to connect and its folder (E14-S07-T02).
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The text after "Offline: ", e.g. <c>drive 'Backup-2TB' (E:\Photos)</c>.</returns>
    public static string? OfflineDetail(MediaItemSummary item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Folder is { } folder
            ? OfflineSource.Detail(folder, item.VolumeLabel, Localizer.Current)
            : OfflineSource.Describe(null, item.VolumeLabel, Localizer.Current);
    }

    private static TileBadge Create(string kind, string iconKey, string labelKey, string? detail)
    {
        var label = Localizer.Current[labelKey];
        if (!Icons.TryGetValue(iconKey, out var icon) || icon is null)
        {
            // A miss isn't kept: before the app's resources load (e.g. a test outside the app) there's no icon yet.
            icon = Application.Current?.TryGetResource(iconKey, null, out var resource) == true ? resource as Geometry : null;
            if (icon is not null)
            {
                Icons[iconKey] = icon;
            }
        }

        return new TileBadge(kind, icon, label, string.IsNullOrWhiteSpace(detail) ? label : $"{label}: {detail}");
    }
}
