using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using Microsoft.Extensions.Logging;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.Views.Libraries;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// Shows the library dialogs on the desktop, modal to the main window. Single-view platforms get pages with E20.
/// </summary>
/// <param name="createLibrary">Makes a fresh create-library view model.</param>
/// <param name="indexedFolders">Makes a fresh indexed folders view model.</param>
/// <param name="logger">Logger, or <c>null</c> for none.</param>
public sealed class LibraryDialogs(Func<CreateLibraryViewModel> createLibrary, Func<IndexedFoldersViewModel> indexedFolders, ILogger<LibraryDialogs>? logger = null)
{
    /// <summary>
    /// How often the folders dialog lists the folders again, so states and counts follow an import.
    /// </summary>
    public static readonly TimeSpan FoldersRefreshInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Raised when the folders dialog closes, e.g. so the navigation panel shows changed folders at once.
    /// </summary>
    public event EventHandler? Closed;

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

    /// <summary>
    /// Shows the indexed folders dialog until the user closes it (E02-S03-T06), adding dropped folders first (E13-S02).
    /// </summary>
    /// <param name="dropped">Folders dropped on the navigation panel, or <c>null</c>.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    public async Task ShowIndexedFoldersAsync(IReadOnlyList<FolderHandle>? dropped = null)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return;
        }

        var viewModel = indexedFolders();
        var window = new IndexedFoldersWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();
        var timer = new DispatcherTimer { Interval = FoldersRefreshInterval };
        timer.Tick += async (_, _) =>
        {
            try
            {
                await viewModel.RefreshAsync();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                // The next tick tries again.
                logger?.LogWarning(exception, "Couldn't refresh the indexed folders");
            }
        };
        await viewModel.OpenAsync();
        if (dropped is { Count: > 0 })
        {
            await viewModel.AddFoldersAsync(dropped);
        }

        timer.Start();
        try
        {
            await window.ShowDialog(owner);
        }
        finally
        {
            timer.Stop();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
