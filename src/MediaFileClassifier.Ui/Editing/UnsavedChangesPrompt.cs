using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Editing;

/// <summary>
/// Shows the unsaved changes prompt as a modal window over the main window.
/// </summary>
internal static class UnsavedChangesPrompt
{
    /// <summary>
    /// Shows the prompt.
    /// </summary>
    /// <param name="prompt">What to ask.</param>
    /// <returns>The user's choice, <see cref="UnsavedChangesChoice.Stay"/> if the window was closed.</returns>
    public static async Task<UnsavedChangesChoice> ShowAsync(UnsavedChangesViewModel prompt)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return UnsavedChangesChoice.Stay;
        }

        var dialog = new Window
        {
            Title = prompt.Title,
            Content = prompt,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        prompt.Answered += (_, choice) => dialog.Close(choice);
        return await dialog.ShowDialog<UnsavedChangesChoice>(owner);
    }
}
