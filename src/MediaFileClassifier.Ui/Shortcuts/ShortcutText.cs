using Avalonia.Input;
using MediaFileClassifier.Ui.Platform;

namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// Writes a shortcut for the shortcut reference (E16-S01-T03): the key as labelled in the current layout, with
/// the Windows-convention name in brackets when it differs, e.g. <c>Ctrl+А (Ctrl+F)</c> on a Russian layout.
/// </summary>
public static class ShortcutText
{
    /// <summary>
    /// Writes a chord.
    /// </summary>
    /// <param name="chord">The keys.</param>
    /// <param name="layout">The current keyboard layout.</param>
    /// <returns>The text.</returns>
    public static string Format(KeyChord chord, IKeyboardLayout layout)
    {
        var latin = chord.ToString();
        var label = layout.GetLabel(chord.Key);
        var qwerty = chord.Key.ToQwertyKeySymbol();
        if (string.IsNullOrWhiteSpace(label) || qwerty is null || string.Equals(label, qwerty, StringComparison.OrdinalIgnoreCase))
        {
            return latin;
        }

        return $"{chord.ModifierPrefix}{label.ToUpperInvariant()} ({latin})";
    }
}
