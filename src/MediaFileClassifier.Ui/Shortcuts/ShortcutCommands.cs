using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// The shortcut system's own command (E16-S01): Help › Keyboard shortcuts.
/// </summary>
public sealed class ShortcutCommands : ICommandContributor
{
    /// <summary>Command ID: open or close the shortcut reference.</summary>
    public const string ShowReference = "help.keyboard-shortcuts";

    private readonly ShortcutReferenceState reference;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShortcutCommands"/> class.
    /// </summary>
    /// <param name="reference">Whether the shortcut reference is open.</param>
    public ShortcutCommands(ShortcutReferenceState reference)
    {
        this.reference = reference;
    }

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        registry.Add(new AppCommand(
            ShowReference, CommandGroups.Help, 10, "Command_KeyboardShortcuts", () => reference.IsOpen = !reference.IsOpen)
        {
            IconKey = "Icon.Help",
        });
    }
}
