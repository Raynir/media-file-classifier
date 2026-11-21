using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// Shows the confirmation before items are removed from the library (E02-S10-T03).
/// </summary>
public interface IRemoveItemsDialog
{
    /// <summary>
    /// Asks the user to confirm.
    /// </summary>
    /// <param name="confirmation">The confirmation; its <see cref="RemoveItemsConfirmationViewModel.DontAskAgain"/> holds the user's choice afterwards.</param>
    /// <returns><see langword="true"/> if the user chose to remove.</returns>
    Task<bool> AskAsync(RemoveItemsConfirmationViewModel confirmation);
}

/// <summary>
/// Shows the confirmation in a modal window over the main window.
/// </summary>
/// <param name="localizer">UI strings.</param>
public sealed class RemoveItemsDialog(Localizer localizer) : IRemoveItemsDialog
{
    /// <inheritdoc />
    public Task<bool> AskAsync(RemoveItemsConfirmationViewModel confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return Task.FromResult(false);
        }

        var dialog = new Window
        {
            Title = localizer["RemoveItems_Title"],
            Content = confirmation,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        confirmation.Answered += (_, remove) => dialog.Close(remove);
        return dialog.ShowDialog<bool>(owner);
    }
}
