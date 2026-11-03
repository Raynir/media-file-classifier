using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.ViewModels.Libraries;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// Shows the welcome screen in the browser area while no library is open (E02-S01-T02), and reopens the last
/// library on startup (E02-S02-T03). The thumbnail grid (E14-S01) takes the area once a library is open.
/// </summary>
public sealed class LibraryShell
{
    private readonly ILibrarySession session;
    private readonly ShellRegions regions;
    private readonly WelcomeViewModel welcome;
    private readonly LibraryOpener opener;
    private readonly Func<ThumbnailGridViewModel> grid;
    private ThumbnailGridViewModel? shownGrid;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryShell"/> class and shows the state of the session.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="regions">The main window areas.</param>
    /// <param name="welcome">The welcome screen.</param>
    /// <param name="opener">Opens libraries.</param>
    /// <param name="grid">Gets the thumbnail grid, once a library opens.</param>
    public LibraryShell(ILibrarySession session, ShellRegions regions, WelcomeViewModel welcome, LibraryOpener opener, Func<ThumbnailGridViewModel> grid)
    {
        this.grid = grid;
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
        if (session.Current is null)
        {
            shownGrid?.Deactivate();
            regions.Browser = welcome;
            _ = welcome.RefreshAsync();
        }
        else
        {
            welcome.Message = null;
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
