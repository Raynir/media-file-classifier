using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace MediaFileClassifier.Ui.Platform;

/// <summary>
/// Puts text on the system clipboard (E14-S04), so commands can be tested without a window.
/// </summary>
public interface ITextClipboard
{
    /// <summary>
    /// Replaces the clipboard's content with text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the text is on the clipboard.</returns>
    Task SetTextAsync(string text);
}

/// <summary>
/// The clipboard of the app's main window, through Avalonia, so it works on every platform head.
/// </summary>
public sealed class AvaloniaTextClipboard : ITextClipboard
{
    /// <inheritdoc />
    public async Task SetTextAsync(string text)
    {
        var topLevel = Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime singleView => TopLevel.GetTopLevel(singleView.MainView),
            _ => null,
        };
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
