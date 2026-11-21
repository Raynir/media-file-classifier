using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Exclusions.ExcludeItems;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The Remove from library command (E02-S10-T03, D024) in the Item menu, the context menu, the details panel's actions
/// and with <c>Delete</c>. It removes the whole selection after one confirmation, which "Don't ask again" turns off.
/// The files stay on disk; the items leave the results, and the selection, through the library's change notice.
/// </summary>
/// <param name="selection">The media browser's selection.</param>
/// <param name="sender">Sends the use cases.</param>
/// <param name="settings">Holds <see cref="AskBeforeRemoving"/>.</param>
/// <param name="dialog">Shows the confirmation.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="logger">Logs a removal that failed.</param>
public sealed partial class RemoveItemsCommands(
    SelectionModel selection,
    ISender sender,
    ISettingsStore settings,
    IRemoveItemsDialog dialog,
    Localizer localizer,
    ILogger<RemoveItemsCommands>? logger = null) : ICommandContributor
{
    /// <summary>Command ID: remove the selected items from the library.</summary>
    public const string Remove = "item.remove";

    /// <summary>
    /// Whether removing items asks first (E02-S10-T03). The confirmation's "Don't ask again" turns it off.
    /// </summary>
    public static readonly BooleanSetting AskBeforeRemoving = new(
        "items.askBeforeRemoving", SettingScope.App, SettingCategories.General, "Setting_AskBeforeRemoving", defaultValue: true)
    {
        HelpKey = "Setting_AskBeforeRemoving_Help",
    };

    private readonly ILogger logger = logger ?? NullLogger<RemoveItemsCommands>.Instance;

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Add(new AppCommand(Remove, CommandGroups.Item, 950, "Command_RemoveFromLibrary", RemoveAsync)
        {
            IconKey = "Icon.Delete",
            AppliesTo = () => selection.Count > 0,
        });
    }

    private async Task RemoveAsync()
    {
        // Menus and shortcuts run the command without a caller to catch, so a failure is logged, not thrown.
        try
        {
            var ids = await sender.Send(new ResolveItemSelectionQuery(selection.Snapshot()));
            if (ids.Count == 0 || !await ConfirmAsync(ids.Count))
            {
                return;
            }

            await sender.Send(new ExcludeItemsCommand(ids));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRemoveFailed(logger, ex);
        }
    }

    private async Task<bool> ConfirmAsync(int count)
    {
        if (!settings.Get(AskBeforeRemoving))
        {
            return true;
        }

        var confirmation = new RemoveItemsConfirmationViewModel(count, localizer);
        if (!await dialog.AskAsync(confirmation))
        {
            return false;
        }

        if (confirmation.DontAskAgain)
        {
            settings.SetValue(AskBeforeRemoving, false);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't remove the selection from the library")]
    private static partial void LogRemoveFailed(ILogger logger, Exception exception);
}
