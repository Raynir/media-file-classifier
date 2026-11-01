using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// Runs commands from key presses in a window (E16-S01-T01, T02). It looks at the physical key (D049) before
/// the focused control sees it, tries the scopes around the focused element and then the global scope, and
/// leaves text editing keys to a focused text field. Esc in a text field reaches shortcuts only if the field
/// doesn't handle it.
/// </summary>
public sealed class ShortcutHandler
{
    private readonly Keymap keymap;
    private readonly ShortcutReferenceState reference;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShortcutHandler"/> class.
    /// </summary>
    /// <param name="keymap">The keymap.</param>
    /// <param name="reference">Whether the shortcut reference is open; while it is, only its own shortcut works.</param>
    public ShortcutHandler(Keymap keymap, ShortcutReferenceState reference)
    {
        this.keymap = keymap;
        this.reference = reference;
    }

    /// <summary>
    /// Starts handling shortcuts in a window or other top-level.
    /// </summary>
    /// <param name="topLevel">The window.</param>
    public void Attach(TopLevel topLevel)
    {
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDownBeforeFocusedControl, RoutingStrategies.Tunnel);
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDownAfterFocusedControl, RoutingStrategies.Bubble);
    }

    private static Control? FocusedElement(object? sender)
        => (sender as TopLevel)?.FocusManager?.GetFocusedElement() as Control;

    private static IEnumerable<string> ScopesAround(Control? focused)
        => focused?.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(Shortcuts.GetScope)
            .OfType<string>() ?? [];

    private void OnKeyDownBeforeFocusedControl(object? sender, KeyEventArgs e)
    {
        var chord = new KeyChord(e.PhysicalKey, e.KeyModifiers);
        var focused = FocusedElement(sender);
        if (e.Handled || (focused is TextBox && TextInputRules.IsReservedForText(chord)))
        {
            return;
        }

        Run(chord, focused, e);
    }

    private void OnKeyDownAfterFocusedControl(object? sender, KeyEventArgs e)
    {
        var focused = FocusedElement(sender);
        if (!e.Handled && e.PhysicalKey == PhysicalKey.Escape && focused is TextBox)
        {
            Run(new KeyChord(e.PhysicalKey, e.KeyModifiers), focused, e);
        }
    }

    private void Run(KeyChord chord, Control? focused, KeyEventArgs e)
    {
        if (chord.Key == PhysicalKey.None || keymap.Find(chord, ScopesAround(focused)) is not { } command)
        {
            return;
        }

        e.Handled = true;
        if (!reference.IsOpen || command.Id == ShortcutCommands.ShowReference)
        {
            _ = command.ExecuteAsync();
        }
    }
}
