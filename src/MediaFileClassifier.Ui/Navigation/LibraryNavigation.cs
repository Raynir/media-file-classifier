using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Duplicates.GetDuplicateStatistics;
using MediatR;

namespace MediaFileClassifier.Ui.Navigation;

/// <summary>
/// The Library section (E13-S02-T02): All media, Recently imported (D053) and Favorites (E11-S01), with their counts.
/// Pending review (E08-S01) adds its own entry later.
/// </summary>
public sealed class LibraryNavigation : INavigationContributor
{
    /// <summary>The section key.</summary>
    public const string SectionKey = "library";

    /// <summary>The key of the All media entry.</summary>
    public const string AllMediaKey = "all";

    /// <summary>The key of the Recently imported entry.</summary>
    public const string RecentlyImportedKey = "recent";

    /// <summary>The key of the Favorites entry.</summary>
    public const string FavoritesKey = "favorites";

    /// <summary>
    /// The key of the Duplicates entry (E12-S05-T03).
    /// </summary>
    public const string DuplicatesKey = "duplicates";

    private readonly ISender sender;
    private readonly TimeProvider clock;
    private readonly NavigationEntry allMedia;
    private readonly NavigationEntry recentlyImported;
    private readonly NavigationEntry favorites;
    private readonly NavigationEntry duplicates;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryNavigation"/> class.
    /// </summary>
    /// <param name="sender">Sends the count queries.</param>
    /// <param name="clock">The clock, for Recently imported.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="pages">Opens the Duplicates page, or <c>null</c> when the entry has nothing to open.</param>
    public LibraryNavigation(ISender sender, TimeProvider clock, Localizer localizer, BrowserPages? pages = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        this.sender = sender;
        this.clock = clock;
        Section = new NavigationSection(SectionKey, localizer.Get("Navigation_Library"), order: 0);
        allMedia = new NavigationEntry(AllMediaKey, localizer.Get("Navigation_AllMedia"), "Icon.Library", BrowseScope.All);
        recentlyImported = new NavigationEntry(RecentlyImportedKey, localizer.Get("Navigation_RecentlyImported"), "Icon.Status.Pending", BrowseScope.RecentlyImported);
        favorites = new NavigationEntry(FavoritesKey, localizer.Get("Navigation_Favorites"), "Icon.Favorite", BrowseScope.Favorites);
        duplicates = new NavigationEntry(DuplicatesKey, localizer.Get("Navigation_Duplicates"), "Icon.Duplicates", scope: null)
        {
            Open = pages is null ? null : pages.ShowDuplicates,
            HidesZeroCount = true,
        };
        Section.Entries.Add(allMedia);
        Section.Entries.Add(recentlyImported);
        Section.Entries.Add(favorites);
        Section.Entries.Add(duplicates);
    }

    /// <inheritdoc />
    public NavigationSection Section { get; }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        allMedia.Count = await sender.Send(new CountLibraryItemsQuery(BrowseScope.All.ToFilter(now)), cancellationToken);
        recentlyImported.Count = await sender.Send(new CountLibraryItemsQuery(BrowseScope.RecentlyImported.ToFilter(now)), cancellationToken);
        favorites.Count = await sender.Send(new CountLibraryItemsQuery(BrowseScope.Favorites.ToFilter(now)), cancellationToken);
        duplicates.Count = (await sender.Send(new GetDuplicateStatisticsQuery(), cancellationToken)).OpenGroups;
    }
}
