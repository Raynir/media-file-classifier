using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Ui.Views.Libraries;

namespace MediaFileClassifier.Ui.Maintenance;

/// <summary>
/// The windows of the library maintenance commands (E02-S06), modal to the main window. Tests replace it to answer
/// for the user.
/// </summary>
public class MaintenanceDialogs
{
    /// <summary>
    /// Gets a value indicating whether the dialogs can be shown: only the desktop app has a main window.
    /// </summary>
    public static bool IsSupported => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    /// <summary>
    /// Asks a question with two answers.
    /// </summary>
    /// <param name="message">The question.</param>
    /// <param name="primary">The primary answer.</param>
    /// <param name="secondary">The other answer.</param>
    /// <returns>Whether the user chose the primary answer.</returns>
    public virtual async Task<bool> AskAsync(string message, string primary, string secondary) =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }
            && await new LibraryMessageWindow(message, primary, secondary).ShowDialog<bool>(owner);

    /// <summary>
    /// Shows a notice with one button.
    /// </summary>
    /// <param name="message">The notice.</param>
    /// <param name="close">The button.</param>
    /// <returns>A task that completes when the notice is closed.</returns>
    public virtual async Task ShowAsync(string message, string close)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            await new LibraryMessageWindow(message, close, null).ShowDialog<bool>(owner);
        }
    }
}
