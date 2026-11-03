using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The media browser's context menu (E14-S04-T01): the Item commands that apply to the selection, in the Item menu's
/// order and bands, with their shortcuts. Stories add entries by registering Item commands with an
/// <see cref="AppCommand.AppliesTo"/> rule (D034); disabled entries explain why in their tooltip.
/// </summary>
/// <param name="commands">The registered commands.</param>
/// <param name="localizer">UI strings.</param>
public sealed class BrowserContextMenu(CommandRegistry commands, Localizer localizer)
{
    /// <summary>
    /// Builds the entries for the current selection.
    /// </summary>
    /// <returns>The entries; empty when nothing applies, so the menu doesn't open.</returns>
    public IReadOnlyList<IMenuEntry> Build()
    {
        // Enter's Open becomes the preview (E14-S05); the menu's Open is "Open in default app".
        var applicable = commands.InGroup(CommandGroups.Item)
            .Where(command => command.Id != BrowserCommands.OpenItem && command.IsApplicable)
            .ToList();
        foreach (var command in applicable)
        {
            command.Refresh();
        }

        return MainMenuViewModel.BuildEntries(applicable, localizer);
    }
}
