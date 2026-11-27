using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Toolbar;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.ViewModels.Navigation;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// Shows the welcome screen in the browser area while no library is open (E02-S01-T02), and reopens the last
/// library on startup (E02-S02-T03). The thumbnail grid (E14-S01) takes the area once a library is open, and the
/// navigation panel and the toolbar (E13-S03) show only while one is.
/// </summary>
public sealed class LibraryShell
{
    private readonly ILibrarySession session;
    private readonly ShellRegions regions;
    private readonly WelcomeViewModel welcome;
    private readonly LibraryOpener opener;
    private readonly Func<ThumbnailGridViewModel> grid;
    private readonly NavigationPanelViewModel? navigation;
    private readonly ToolbarViewModel? toolbar;
    private readonly BrowserPages? pages;
    private ThumbnailGridViewModel? shownGrid;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryShell"/> class and shows the state of the session.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="regions">The main window areas.</param>
    /// <param name="welcome">The welcome screen.</param>
    /// <param name="opener">Opens libraries.</param>
    /// <param name="grid">Gets the thumbnail grid, once a library opens.</param>
    /// <param name="navigation">The navigation panel, shown while a library is open, or <c>null</c> for none.</param>
    /// <param name="toolbar">The toolbar, shown while a library is open, or <c>null</c> for none.</param>
    /// <param name="pages">Swaps pages such as Duplicates with the media browser, or <c>null</c> for none.</param>
    public LibraryShell(ILibrarySession session, ShellRegions regions, WelcomeViewModel welcome, LibraryOpener opener, Func<ThumbnailGridViewModel> grid, NavigationPanelViewModel? navigation = null, ToolbarViewModel? toolbar = null, BrowserPages? pages = null)
    {
        this.pages = pages;
        this.toolbar = toolbar;
        this.grid = grid;
        this.navigation = navigation;
        this.session = session;
        this.regions = regions;
        this.welcome = welcome;
        this.opener = opener;
        session.CurrentChanged += (_, _) =>
        {
            // Libraries open on background threads.
            if (Dispatcher.UIThread.CheckAccess())
            {
                Update();
            }
            else
            {
                Dispatcher.UIThread.Post(Update);
            }
        };
        opener.Failed += (_, message) => welcome.Message = message;
        Update();
    }

    /// <summary>
    /// Reopens the last library, if the setting allows, once the main window is up.
    /// </summary>
    public void Start() => Dispatcher.UIThread.Post(() => _ = opener.OpenLastAsync());

    private void Update()
    {
        // A library change shows the media browser again, so a page of the old library never stays.
        pages?.ShowBrowser();
        if (session.Current is null)
        {
            shownGrid?.Deactivate();
            regions.Navigation = null;
            regions.Toolbar = null;
            regions.Browser = welcome;
            _ = welcome.RefreshAsync();
        }
        else
        {
            welcome.Message = null;
            regions.Navigation = navigation;
            regions.Toolbar = toolbar;
            shownGrid ??= grid();
            if (regions.Browser == welcome || regions.Browser is null)
            {
                regions.Browser = shownGrid;
            }

            shownGrid.Deactivate();
            _ = shownGrid.ActivateAsync();
        }
    }
}
