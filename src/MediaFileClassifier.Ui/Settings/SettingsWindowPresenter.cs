using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.Ui.Views.Settings;

namespace MediaFileClassifier.Ui.Settings;

/// <summary>
/// Opens the settings window on the desktop, owned by the main window. Only one is open at a time.
/// </summary>
/// <param name="viewModel">The settings view model.</param>
public sealed class SettingsWindowPresenter(SettingsViewModel viewModel)
{
    private SettingsWindow? window;

    /// <summary>
    /// Gets a value indicating whether the app has windows, so the settings window can be shown.
    /// </summary>
    public static bool IsSupported => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    /// <summary>
    /// Shows the settings window, or brings it to the front if it's already open.
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

        window = new SettingsWindow { DataContext = viewModel };
        window.Closed += (_, _) => window = null;
        window.Show(owner);
    }
}
