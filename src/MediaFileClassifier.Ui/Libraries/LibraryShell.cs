using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels.Libraries;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// Shows the welcome screen in the browser area while no library is open (E02-S01-T02). The media browser (E14)
/// takes the area once a library is open.
/// </summary>
public sealed class LibraryShell
{
    private readonly ILibrarySession session;
    private readonly ShellRegions regions;
    private readonly WelcomeViewModel welcome;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryShell"/> class and shows the state of the session.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="regions">The main window areas.</param>
    /// <param name="welcome">The welcome screen.</param>
    public LibraryShell(ILibrarySession session, ShellRegions regions, WelcomeViewModel welcome)
    {
        this.session = session;
        this.regions = regions;
        this.welcome = welcome;
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
        Update();
    }

    private void Update()
    {
        if (session.Current is null)
        {
            regions.Browser = welcome;
        }
        else if (regions.Browser == welcome)
        {
            regions.Browser = null;
        }
    }
}
