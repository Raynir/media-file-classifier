using Avalonia.Input;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Settings;
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
        // Library (E02-S03): Add folder, also on the toolbar (E13-S03).
        Bind(LibraryCommands.AddFolder, PhysicalKey.O, KeyModifiers.Control | KeyModifiers.Shift),

        // View (E13-S01)
        Bind(ShellCommands.ToggleNavigation, PhysicalKey.Digit1, KeyModifiers.Control),
        Bind(ShellCommands.ToggleDetails, PhysicalKey.Digit2, KeyModifiers.Control),
        Bind(ShellCommands.CollapseNavigation, PhysicalKey.Digit1, KeyModifiers.Control | KeyModifiers.Shift),
        Bind(ShellCommands.CollapseDetails, PhysicalKey.Digit2, KeyModifiers.Control | KeyModifiers.Shift),

        // Media browser (E14-S01): thumbnail size while the grid has focus (D046), and opening the focused item.
        Bind(BrowserCommands.LargerThumbnails, PhysicalKey.Equal, scope: ShortcutScopes.Browser),
        Bind(BrowserCommands.SmallerThumbnails, PhysicalKey.Minus, scope: ShortcutScopes.Browser),
        Bind(BrowserCommands.OpenItem, PhysicalKey.Enter, scope: ShortcutScopes.Browser),
        Bind(BrowserCommands.SelectAll, PhysicalKey.A, KeyModifiers.Control, ShortcutScopes.Browser),
        Bind(BrowserCommands.SelectNone, PhysicalKey.Escape, scope: ShortcutScopes.Browser),

        // Item commands (E14-S04): Ctrl+O anywhere (E16-S02); Copy file path as in File Explorer.
        Bind(ItemCommands.OpenInDefaultApp, PhysicalKey.O, KeyModifiers.Control),
        Bind(ItemCommands.Reveal, PhysicalKey.R, KeyModifiers.Control | KeyModifiers.Shift, ShortcutScopes.Browser),
        Bind(ItemCommands.CopyPath, PhysicalKey.C, KeyModifiers.Control | KeyModifiers.Shift, ShortcutScopes.Browser),
        Bind(FindSimilarCommands.FindSimilar, PhysicalKey.F, KeyModifiers.Control | KeyModifiers.Shift, ShortcutScopes.Browser),

        // Preview and details panel (E14-S05): previous and next, and Enter opens the item in its default app.
        Bind(BrowserCommands.PreviousItem, PhysicalKey.ArrowLeft, scope: ShortcutScopes.Viewer),
        Bind(BrowserCommands.NextItem, PhysicalKey.ArrowRight, scope: ShortcutScopes.Viewer),
        Bind(ItemCommands.OpenInDefaultApp, PhysicalKey.Enter, scope: ShortcutScopes.Viewer),

        // Tools (E17-S01)
        Bind(SettingsCommands.OpenSettings, PhysicalKey.Comma, KeyModifiers.Control),

        // Help (E16-S01): F1, or ? on a US keyboard.
        Bind(ShortcutCommands.ShowReference, PhysicalKey.F1),
        Bind(ShortcutCommands.ShowReference, PhysicalKey.Slash, KeyModifiers.Shift),
    ];

    private static ShortcutBinding Bind(
        string commandId, PhysicalKey key, KeyModifiers modifiers = KeyModifiers.None, string scope = ShortcutScopes.Global)
        => new(commandId, scope, new KeyChord(key, modifiers));
}
