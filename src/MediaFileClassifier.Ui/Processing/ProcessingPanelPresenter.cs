using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MediaFileClassifier.Ui.ViewModels.Processing;
using MediaFileClassifier.Ui.Views.Processing;

namespace MediaFileClassifier.Ui.Processing;

/// <summary>
/// Opens the processing panel in its own window on the desktop (E05-S03), owned by the main window but not modal, so
/// the user can keep working. Only one is open at a time. The window only looks at the jobs: closing it, or the app
/// never having opened it, changes nothing about them.
/// </summary>
/// <param name="viewModel">The processing panel's view model.</param>
public sealed class ProcessingPanelPresenter(ProcessingPanelViewModel viewModel)
{
    /// <summary>
    /// How often the window lists the jobs again, so states and the recent list follow the work. Counts and the time
    /// left follow the job progress in between.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private ProcessingPanelWindow? window;

    /// <summary>
    /// Gets a value indicating whether the app has windows, so the panel can be shown.
    /// </summary>
    public static bool IsSupported => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    /// <summary>
    /// Shows the processing panel, or brings it to the front if it's already open.
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

        var opened = new ProcessingPanelWindow { DataContext = viewModel };
        var timer = new DispatcherTimer { Interval = RefreshInterval };
        timer.Tick += async (_, _) => await viewModel.RefreshAsync();
        EventHandler requested = (_, _) => opened.Close();
        viewModel.CloseRequested += requested;
        opened.Closed += (_, _) =>
        {
            timer.Stop();
            viewModel.Detach();
            viewModel.CloseRequested -= requested;
            window = null;
        };
        window = opened;
        viewModel.Attach();
        timer.Start();
        opened.Show(owner);
        _ = viewModel.RefreshAsync();
    }
}
