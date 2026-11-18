using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.UseCases.Browsing;

/// <summary>
/// What the media browser shows: the scope of the selected navigation entry (E13-S02-T02). Search and filters apply
/// within it (E09). Other Epics add their own scopes, such as a collection (E11).
/// </summary>
public abstract record BrowseScope
{
    /// <summary>
    /// Gets the scope of every item, the default.
    /// </summary>
    public static BrowseScope All { get; } = new AllItemsScope();

    /// <summary>
    /// Gets the scope of the items imported in the last <see cref="RecentlyImportedScope.Days"/> days (D053).
    /// </summary>
    public static BrowseScope RecentlyImported { get; } = new RecentlyImportedScope();

    /// <summary>
    /// Gets the scope of the favorites (E11-S01).
    /// </summary>
    public static BrowseScope Favorites { get; } = new FavoritesScope();

    /// <summary>
    /// Gets the filter for the item queries.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>The filter.</returns>
    public abstract MediaItemFilter ToFilter(DateTimeOffset now);
}

/// <summary>
/// Every item of the library.
/// </summary>
public sealed record AllItemsScope : BrowseScope
{
    /// <inheritdoc />
    public override MediaItemFilter ToFilter(DateTimeOffset now) => MediaItemFilter.All;
}

/// <summary>
/// The items imported in the last 7 days, newest first (D053).
/// </summary>
public sealed record RecentlyImportedScope : BrowseScope
{
    /// <summary>
    /// How many days count as recent (D053). A setting can come later.
    /// </summary>
    public const int Days = 7;

    /// <inheritdoc />
    public override MediaItemFilter ToFilter(DateTimeOffset now) => new() { ImportedSince = now.AddDays(-Days) };
}

/// <summary>
/// The items the user marked as favorites (E11-S01).
/// </summary>
public sealed record FavoritesScope : BrowseScope
{
    /// <inheritdoc />
    public override MediaItemFilter ToFilter(DateTimeOffset now) => new() { IsFavorite = true };
}

/// <summary>
/// The items of an indexed folder, or of one of its subfolders with everything inside it (E13-S02-T03).
/// </summary>
/// <param name="FolderId">The indexed folder.</param>
/// <param name="Subfolder">The subfolder, with <c>/</c> separators; empty for the whole folder.</param>
public sealed record FolderScope(Id<IndexedFolder> FolderId, string Subfolder = "") : BrowseScope
{
    /// <inheritdoc />
    public override MediaItemFilter ToFilter(DateTimeOffset now) => new() { FolderId = FolderId, Subfolder = Subfolder };
}
