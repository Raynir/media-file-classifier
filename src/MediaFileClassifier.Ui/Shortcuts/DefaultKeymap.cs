using Avalonia.Input;
using MediaFileClassifier.Ui.Shell;

namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// The default keymap (E16-S01): every shortcut of the app, in one place. It follows the E16-S02 table, and
/// each Story that adds a command adds its row here (D034). Keys are physical keys (D049): name them after
/// their position on a US keyboard.
/// </summary>
public static class DefaultKeymap
{
    /// <summary>
    /// Gets the default bindings. A command with several bindings shows the first in menus and tooltips.
    /// </summary>
    public static IReadOnlyList<ShortcutBinding> Bindings { get; } =
    [
        // View (E13-S01)
        Bind(ShellCommands.ToggleNavigation, PhysicalKey.Digit1, KeyModifiers.Control),
        Bind(ShellCommands.ToggleDetails, PhysicalKey.Digit2, KeyModifiers.Control),
        Bind(ShellCommands.CollapseNavigation, PhysicalKey.Digit1, KeyModifiers.Control | KeyModifiers.Shift),
        Bind(ShellCommands.CollapseDetails, PhysicalKey.Digit2, KeyModifiers.Control | KeyModifiers.Shift),

        // Help (E16-S01): F1, or ? on a US keyboard.
        Bind(ShortcutCommands.ShowReference, PhysicalKey.F1),
        Bind(ShortcutCommands.ShowReference, PhysicalKey.Slash, KeyModifiers.Shift),
    ];

    private static ShortcutBinding Bind(
        string commandId, PhysicalKey key, KeyModifiers modifiers = KeyModifiers.None, string scope = ShortcutScopes.Global)
        => new(commandId, scope, new KeyChord(key, modifiers));
}
