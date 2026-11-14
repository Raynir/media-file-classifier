using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Views.Libraries;

namespace MediaFileClassifier.Ui.Analysis;

/// <summary>
/// Shows the analysis dialogs in the library message window over the main window.
/// </summary>
/// <param name="localizer">UI strings.</param>
public sealed class AnalysisDialogs(Localizer localizer) : IAnalysisDialogs
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string message, string confirmLabel, string cancelLabel) => ShowAsync(message, confirmLabel, cancelLabel);

    /// <inheritdoc />
    public async Task ShowAsync(string message) => await ShowAsync(message, localizer.Get("Folders_Close"), null);

    private static async Task<bool> ShowAsync(string message, string primary, string? secondary)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return false;
        }

        return await new LibraryMessageWindow(message, primary, secondary).ShowDialog<bool>(owner);
    }
}
