using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Ui.Editing;
using MediaFileClassifier.Ui.Localization;
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
/// <param name="guard">Asks about staged changes before a new library replaces the open one (E07-S12-T01).</param>
/// <param name="deleteLibrary">Makes a fresh delete-library view model, or <c>null</c> where deleting isn't offered.</param>
/// <param name="localizer">UI strings, for the notice after deleting, or <c>null</c> for none.</param>
public sealed class LibraryDialogs(
    Func<CreateLibraryViewModel> createLibrary,
    Func<IndexedFoldersViewModel> indexedFolders,
    ILogger<LibraryDialogs>? logger = null,
    UnsavedChangesGuard? guard = null,
    Func<DeleteLibraryViewModel>? deleteLibrary = null,
    Localizer? localizer = null)
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

        if (guard is not null && !await guard.ConfirmLeaveAsync())
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
    /// Shows the delete-library confirmation (E02-S07-T02) until the library is deleted or the user cancels. Deleting the
    /// open library asks about staged changes first, since it closes the library.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <param name="name">The library's name as the caller knows it, shown if the library can't be read.</param>
    /// <param name="isOpen">Whether it's the open library.</param>
    /// <returns><c>true</c> if the library was deleted.</returns>
    public async Task<bool> ShowDeleteLibraryAsync(string libraryDirectory, string name, bool isOpen)
    {
        if (deleteLibrary is null
            || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return false;
        }

        if (isOpen && guard is not null && !await guard.ConfirmLeaveAsync())
        {
            return false;
        }

        var viewModel = deleteLibrary();
        await viewModel.LoadAsync(libraryDirectory, name);
        var window = new DeleteLibraryWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();
        await window.ShowDialog(owner);
        if (viewModel.Result is not { Deleted: true } result)
        {
            return false;
        }

        if (!result.FolderRemoved && localizer is not null)
        {
            // Not an error: the user's own files stay, and the folder with them.
            var notice = new LibraryMessageWindow(localizer.Format("DeleteLibrary_KeptFiles", libraryDirectory), localizer.Get("DeleteLibrary_KeptFilesOk"), null);
            await notice.ShowDialog(owner);
        }

        return true;
    }

    /// <summary>
    /// Shows the indexed folders dialog until the user closes it (E02-S03-T06), adding dropped folders first (E13-S02).
    /// </summary>
    /// <param name="dropped">Folders dropped on the navigation panel, or <c>null</c>.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    public async Task ShowIndexedFoldersAsync(IReadOnlyList<FolderHandle>? dropped = null) => await ShowFoldersAsync(dropped);

    /// <summary>
    /// Shows the indexed folders dialog with dropped folders added (E03-S06), and says how many folders were added by the
    /// time it closed.
    /// </summary>
    /// <param name="dropped">The dropped folders.</param>
    /// <returns>The number of folders added while the dialog was open.</returns>
    public Task<int> AddDroppedFoldersAsync(IReadOnlyList<FolderHandle> dropped) => ShowFoldersAsync(dropped);

    private async Task<int> ShowFoldersAsync(IReadOnlyList<FolderHandle>? dropped)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return 0;
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

        return viewModel.AddedCount;
    }
}
