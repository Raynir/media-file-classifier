using Avalonia.Controls;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// Closing the main window while work is pending (D027, E05-S01-T07): asks first, unless the user chose
/// "Don't ask again", then stops the jobs cleanly before the window closes. The work resumes the next time the library
/// is opened (E05-S01-T05).
/// </summary>
/// <param name="jobs">The job system.</param>
/// <param name="settings">App settings, for "Don't ask again".</param>
/// <param name="localizer">UI strings.</param>
public sealed class QuitGuard(IJobSystem jobs, ISettingsService settings, Localizer localizer)
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

    private async Task CloseWhenReadyAsync(Window window)
    {
        if (await ConfirmAndStopAsync(confirmation => ShowDialogAsync(window, confirmation, localizer["QuitConfirmation_Title"])).ConfigureAwait(true))
        {
            readyToClose = true;
            window.Close();
        }
    }
}
