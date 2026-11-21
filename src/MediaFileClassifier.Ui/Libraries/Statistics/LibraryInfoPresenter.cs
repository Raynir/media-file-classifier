using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.Views.Libraries;

namespace MediaFileClassifier.Ui.Libraries.Statistics;

/// <summary>
/// Opens the library information window (E02-S05-T02): non-modal and owned by the main window, one at a time, like
/// the processing queue. Opened from View › Library information and from the status bar summary.
/// </summary>
/// <param name="viewModel">The library information.</param>
public sealed class LibraryInfoPresenter(LibraryInfoViewModel viewModel)
{
    private LibraryInfoWindow? window;

    /// <summary>
    /// Gets a value indicating whether the window can be shown: only on the desktop.
    /// </summary>
    public static bool IsSupported => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    /// <summary>
    /// Shows the window, or brings it to the front when it's already open.
    /// </summary>
    public void Show()
    {
        if (window is not null)
        {
            window.Activate();
            return;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return;
        }

        var opened = new LibraryInfoWindow { DataContext = viewModel };
        opened.Closed += (_, _) =>
        {
            viewModel.Detach();
            window = null;
        };
        window = opened;
        viewModel.Attach();
        opened.Show(owner);
    }
}
