using Avalonia.Input;

namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// Which keys belong to a text field while it has focus (E16-S01-T02): text editing keys always win there.
/// </summary>
public static class TextInputRules
{
    // Ctrl and Ctrl+Shift chords that text fields use: clipboard, undo, select all and moving by words.
    private static readonly HashSet<PhysicalKey> EditingChordKeys =
    [
        PhysicalKey.A, PhysicalKey.C, PhysicalKey.X, PhysicalKey.V, PhysicalKey.Z, PhysicalKey.Y,
        PhysicalKey.ArrowLeft, PhysicalKey.ArrowRight, PhysicalKey.ArrowUp, PhysicalKey.ArrowDown,
        PhysicalKey.Home, PhysicalKey.End, PhysicalKey.Backspace, PhysicalKey.Delete, PhysicalKey.Insert,
    ];

    /// <summary>
    /// Checks whether a text field keeps the keys, so no shortcut runs: every key without Ctrl or Alt
    /// (typing, Shift+key, Enter, arrows, Delete, Esc) except function keys, and the Ctrl and Ctrl+Shift
    /// editing chords. Esc reaches shortcuts only if the text field doesn't handle it.
    /// </summary>
    /// <param name="chord">The keys pressed.</param>
    /// <returns>Whether the text field keeps them.</returns>
    public static bool IsReservedForText(KeyChord chord)
    {
        if ((chord.Modifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) != 0)
        {
            return false;
        }

        if ((chord.Modifiers & KeyModifiers.Control) == 0)
        {
            return !IsFunctionKey(chord.Key);
        }

        return EditingChordKeys.Contains(chord.Key);
    }

    private static bool IsFunctionKey(PhysicalKey key) => key is >= PhysicalKey.F1 and <= PhysicalKey.F24;
}
