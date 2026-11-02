using Avalonia.Input;

namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// A physical key with modifiers (D049). It names the key's position, not the character it types, so
/// <c>Ctrl+F</c> is the same keys on English and Russian layouts.
/// </summary>
/// <param name="Key">The physical key, named after its position on a US keyboard.</param>
/// <param name="Modifiers">The modifier keys held down.</param>
public readonly record struct KeyChord(PhysicalKey Key, KeyModifiers Modifiers = KeyModifiers.None)
{
    /// <summary>
    /// Gets the chord as a gesture with the US key, for menus and tooltips, e.g. <c>Ctrl+1</c>.
    /// </summary>
    /// <returns>The gesture.</returns>
    public KeyGesture ToKeyGesture() => new(Key.ToQwertyKey(), Modifiers);

    /// <summary>
    /// Gets the name of the key alone, as on a US keyboard, e.g. <c>F</c>, <c>1</c>, <c>/</c> or <c>F1</c>.
    /// </summary>
    public string KeyName => Key switch
    {
        PhysicalKey.Escape => "Esc",
        PhysicalKey.Delete => "Del",
        PhysicalKey.Insert => "Ins",
        PhysicalKey.Space => "Space",
        PhysicalKey.Enter => "Enter",
        PhysicalKey.Tab => "Tab",
        PhysicalKey.Backspace => "Backspace",
        PhysicalKey.ArrowLeft => "Left",
        PhysicalKey.ArrowRight => "Right",
        PhysicalKey.ArrowUp => "Up",
        PhysicalKey.ArrowDown => "Down",
        PhysicalKey.PageUp => "PgUp",
        PhysicalKey.PageDown => "PgDn",
        _ => Key.ToQwertyKeySymbol()?.ToUpperInvariant() ?? Key.ToString(),
    };

    /// <summary>
    /// Gets the modifiers in Windows order, each followed by a plus, e.g. <c>Ctrl+Shift+</c>.
    /// </summary>
    public string ModifierPrefix
        => (Modifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : string.Empty)
            + (Modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : string.Empty)
            + (Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : string.Empty)
            + (Modifiers.HasFlag(KeyModifiers.Meta) ? "Win+" : string.Empty);

    /// <summary>
    /// Writes the chord with Windows-convention names, e.g. <c>Ctrl+Shift+1</c> (D028).
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString() => ModifierPrefix + KeyName;
}
