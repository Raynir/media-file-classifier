using Avalonia.Controls;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Editing;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// Closing the main window while work is pending (D027, E05-S01-T07): asks first, unless the user chose
/// "Don't ask again", then stops the jobs cleanly before the window closes. The work resumes the next time the library
/// is opened (E05-S01-T05).
/// </summary>
/// <param name="jobs">The job system.</param>
/// <param name="settings">App settings, for "Don't ask again".</param>
/// <param name="localizer">UI strings.</param>
/// <param name="logger">Logger.</param>
/// <param name="unsaved">Asks about staged changes before the window closes (E07-S12-T01).</param>
public sealed partial class QuitGuard(
    IJobSystem jobs, ISettingsService settings, Localizer localizer, ILogger<QuitGuard> logger, UnsavedChangesGuard? unsaved = null)
{
    private bool readyToClose;

    /// <summary>
    /// Guards a window's closing.
    /// </summary>
    /// <param name="window">The main window.</param>
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.Closing += (_, e) =>
        {
            if (!readyToClose && unsaved is { HasUnsavedChanges: true })
            {
                // Staged changes come first. The window closes again once they are saved or dropped, and then the
                // jobs are dealt with below.
                e.Cancel = true;
                _ = CloseWhenSavedAsync(window, unsaved);
                return;
            }

            if (readyToClose || jobs.Current is null)
            {
                return;
            }

            // Closing can't wait, so it's cancelled and the window is closed again once the jobs have stopped.
            e.Cancel = true;
            _ = CloseWhenReadyAsync(window);
        };
    }

    /// <summary>
    /// Decides whether to quit, and stops the jobs if so.
    /// </summary>
    /// <param name="ask">Shows the confirmation and returns whether the user chose to quit.</param>
    /// <returns><see langword="true"/> when the jobs are stopped and the app can close.</returns>
    public async Task<bool> ConfirmAndStopAsync(Func<QuitConfirmationViewModel, Task<bool>> ask)
    {
        ArgumentNullException.ThrowIfNull(ask);

        var pending = await jobs.CountPendingAsync().ConfigureAwait(true);
        if (pending > 0 && settings.Current.Processing.ConfirmQuitWhileProcessing)
        {
            var confirmation = new QuitConfirmationViewModel(pending, localizer);
            if (!await ask(confirmation).ConfigureAwait(true))
            {
                return false;
            }

            if (confirmation.DontAskAgain)
            {
                settings.Save(settings.Current with { Processing = settings.Current.Processing with { ConfirmQuitWhileProcessing = false } });
            }
        }

        await jobs.StopAsync().ConfigureAwait(true);
        return true;
    }

    private static Task<bool> ShowDialogAsync(Window owner, QuitConfirmationViewModel confirmation, string title)
    {
        var dialog = new Window
        {
            Title = title,
            Content = confirmation,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        confirmation.Answered += (_, quit) => dialog.Close(quit);
        return dialog.ShowDialog<bool>(owner);
    }

    private static async Task CloseWhenSavedAsync(Window window, UnsavedChangesGuard unsaved)
    {
        if (await unsaved.ConfirmLeaveAsync().ConfigureAwait(true))
        {
            window.Close();
        }
    }

    private async Task CloseWhenReadyAsync(Window window)
    {
        bool quit;
        try
        {
            quit = await ConfirmAndStopAsync(confirmation => ShowDialogAsync(window, confirmation, localizer["QuitConfirmation_Title"])).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // The app must still close; interrupted work is queued again when the library opens (E05-S01-T05).
            LogStopFailed(exception);
            quit = true;
        }

        if (quit)
        {
            readyToClose = true;
            window.Close();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The jobs couldn't be stopped before quitting")]
    private partial void LogStopFailed(Exception exception);
}
