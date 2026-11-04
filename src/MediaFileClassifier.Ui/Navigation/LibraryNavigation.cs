using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediatR;

namespace MediaFileClassifier.Ui.Navigation;

/// <summary>
/// The Library section (E13-S02-T02): All media and Recently imported (D053), with their counts. Favorites (E11-S01)
/// and Pending review (E08-S01) add their own entries later.
/// </summary>
public sealed class LibraryNavigation : INavigationContributor
{
    /// <summary>The section key.</summary>
    public const string SectionKey = "library";

    /// <summary>The key of the All media entry.</summary>
    public const string AllMediaKey = "all";

    /// <summary>The key of the Recently imported entry.</summary>
    public const string RecentlyImportedKey = "recent";

    private readonly ISender sender;
    private readonly TimeProvider clock;
    private readonly NavigationEntry allMedia;
    private readonly NavigationEntry recentlyImported;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryNavigation"/> class.
    /// </summary>
    /// <param name="sender">Sends the count queries.</param>
    /// <param name="clock">The clock, for Recently imported.</param>
    /// <param name="localizer">UI strings.</param>
    public LibraryNavigation(ISender sender, TimeProvider clock, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        this.sender = sender;
        this.clock = clock;
        Section = new NavigationSection(SectionKey, localizer.Get("Navigation_Library"), order: 0);
        allMedia = new NavigationEntry(AllMediaKey, localizer.Get("Navigation_AllMedia"), "Icon.Library", BrowseScope.All);
        recentlyImported = new NavigationEntry(RecentlyImportedKey, localizer.Get("Navigation_RecentlyImported"), "Icon.Status.Pending", BrowseScope.RecentlyImported);
        Section.Entries.Add(allMedia);
        Section.Entries.Add(recentlyImported);
    }

    /// <inheritdoc />
    public NavigationSection Section { get; }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        allMedia.Count = await sender.Send(new CountLibraryItemsQuery(BrowseScope.All.ToFilter(now)), cancellationToken);
        recentlyImported.Count = await sender.Send(new CountLibraryItemsQuery(BrowseScope.RecentlyImported.ToFilter(now)), cancellationToken);
    }
}
