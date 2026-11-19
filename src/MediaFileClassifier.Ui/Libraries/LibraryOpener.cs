using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Editing;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Views.Libraries;
using MediaFileClassifier.UseCases.Libraries.OpenLibrary;
using MediatR;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// Opens libraries from the UI (E02-S02): shows a modal progress window while a migration runs, offers to restore
/// the backup when it fails, and reports other failures on the welcome screen.
/// </summary>
/// <param name="sender">Sends the library commands.</param>
/// <param name="platform">Folder picker.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="guard">Asks about staged changes before another library replaces the open one (E07-S12-T01).</param>
public sealed class LibraryOpener(ISender sender, IPlatformServices platform, Localizer localizer, UnsavedChangesGuard? guard = null)
{
    /// <summary>
    /// Raised with a message when a library couldn't be opened; the welcome screen shows it.
    /// </summary>
    public event EventHandler<string>? Failed;

    /// <summary>
    /// Gets or sets how the user is asked to restore a backup after a failed migration. Tests replace it.
    /// </summary>
    public Func<string, Task<bool>> ConfirmRestore { get; set; } = ShowRestoreDialogAsync;

    /// <summary>
    /// Asks for a library folder and opens it.
    /// </summary>
    /// <returns>A task that completes when the library is open, or the attempt failed.</returns>
    public async Task PickAndOpenAsync()
    {
        if (!await CanLeaveAsync())
        {
            return;
        }

        if (await platform.PickFolderAsync(localizer.Get("OpenLibrary_Pick")) is { } folder)
        {
            await OpenAsync(folder.Id);
        }
    }

    /// <summary>
    /// Opens a library.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <returns>A task that completes when the library is open, or the attempt failed.</returns>
    public async Task OpenAsync(string libraryDirectory)
    {
        if (!await CanLeaveAsync())
        {
            return;
        }

        using var progress = new MigrationProgress();
        await HandleAsync(await sender.Send(new OpenLibraryCommand(libraryDirectory, progress)));
    }

    /// <summary>
    /// Reopens the last library on startup, if the setting allows.
    /// </summary>
    /// <returns>A task that completes when the library is open, or the attempt failed.</returns>
    public async Task OpenLastAsync()
    {
        using var progress = new MigrationProgress();
        if (await sender.Send(new OpenLastLibraryCommand(progress)) is { } result)
        {
            await HandleAsync(result);
        }
    }

    // Switching library drops what the editor holds, so staged changes are asked about first (E07-S12-T01).
    private Task<bool> CanLeaveAsync() => guard?.ConfirmLeaveAsync() ?? Task.FromResult(true);

    private static async Task<bool> ShowRestoreDialogAsync(string message)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return false;
        }

        return await new LibraryMessageWindow(message, Localizer.Current.Get("MigrationFailed_Restore"), Localizer.Current.Get("MigrationFailed_Keep"))
            .ShowDialog<bool>(owner);
    }

    private async Task HandleAsync(OpenLibraryResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(result.LibraryDirectory));
        if (result is { Error: LibraryOpenError.MigrationFailed, BackupPath: { } backup }
            && await ConfirmRestore(localizer.Format("MigrationFailed_Question", name)))
        {
            var restored = await sender.Send(new RestoreLibraryBackupCommand(result.LibraryDirectory, backup));
            Failed?.Invoke(this, localizer.Format(restored ? "MigrationFailed_Restored" : "MigrationFailed_RestoreFailed", name));
            return;
        }

        Failed?.Invoke(this, localizer.Format("OpenLibrary_Failed", name, localizer.Get("LibraryOpenError_" + result.Error)));
    }

    /// <summary>
    /// Shows the modal "Updating library" window from the first migration report until disposed.
    /// </summary>
    private sealed class MigrationProgress : IProgress<LibraryOpenStage>, IDisposable
    {
        private Window? window;

        public void Report(LibraryOpenStage value) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (window is null && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            {
                window = new LibraryProgressWindow();
                _ = window.ShowDialog(owner);
            }
        });

        public void Dispose() => Avalonia.Threading.Dispatcher.UIThread.Post(() => window?.Close());
    }
}
