using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Ui.ViewModels.Activity;
using MediaFileClassifier.Ui.Views.Activity;

namespace MediaFileClassifier.Ui.Activity;

/// <summary>
/// Opens the activity log in its own window on the desktop (E15-S04), owned by the main window but not modal, so the
/// user can keep working and watch events arrive. Only one is open at a time. The window only reads the log.
/// </summary>
/// <param name="viewModel">The activity log's view model.</param>
public sealed class ActivityLogPresenter(ActivityLogViewModel viewModel)
{
    private ActivityLogWindow? window;

    /// <summary>
    /// Gets a value indicating whether the app has windows, so the log can be shown.
    /// </summary>
    public static bool IsSupported => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    /// <summary>
    /// Shows the activity log, or brings it to the front if it's already open.
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

        var opened = new ActivityLogWindow { DataContext = viewModel };
        EventHandler requested = (_, _) => opened.Close();
        viewModel.CloseRequested += requested;
        opened.Closed += (_, _) =>
        {
            viewModel.Detach();
            viewModel.CloseRequested -= requested;
            window = null;
        };
        window = opened;
        viewModel.Attach();
        opened.Show(owner);
        _ = viewModel.RefreshAsync();
    }
}
