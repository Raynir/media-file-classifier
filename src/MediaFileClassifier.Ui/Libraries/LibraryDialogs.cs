using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.Views.Libraries;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// Shows the library dialogs on the desktop, modal to the main window. Single-view platforms get pages with E20.
/// </summary>
/// <param name="createLibrary">Makes a fresh create-library view model.</param>
public sealed class LibraryDialogs(Func<CreateLibraryViewModel> createLibrary)
{
    /// <summary>
    /// Gets a value indicating whether the app has windows, so the dialogs can be shown.
    /// </summary>
    public static bool IsSupported => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    /// <summary>
    /// Shows the create-library dialog until the user finishes or cancels it.
    /// </summary>
    /// <returns>A task that completes when the dialog closes.</returns>
    public async Task ShowCreateLibraryAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return;
        }

        var viewModel = createLibrary();
        var window = new CreateLibraryWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();
        await viewModel.ValidateAsync();
        await window.ShowDialog(owner);
    }
}
