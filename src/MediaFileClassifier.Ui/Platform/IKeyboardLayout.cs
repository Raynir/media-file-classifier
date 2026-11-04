using Avalonia.Input;

namespace MediaFileClassifier.Ui.Platform;

/// <summary>
/// Tells what a physical key types in the current keyboard layout, so the shortcut reference can label keys
/// as the user sees them (D049). The desktop head asks Windows; other heads use <see cref="QwertyKeyboardLayout"/>.
/// </summary>
public interface IKeyboardLayout
{
    /// <summary>
    /// Gets the character a key types without modifiers in the current layout, e.g. <c>ф</c> for
    /// <see cref="PhysicalKey.A"/> on a Russian layout.
    /// </summary>
    /// <param name="key">The physical key.</param>
    /// <returns>The character, or <c>null</c> for a key that doesn't type one, such as F1.</returns>
    string? GetLabel(PhysicalKey key);
}

/// <summary>
/// A US keyboard layout: each key types what its position is named after.
/// </summary>
public sealed class QwertyKeyboardLayout : IKeyboardLayout
{
    /// <inheritdoc />
    public string? GetLabel(PhysicalKey key) => key == PhysicalKey.Space ? null : key.ToQwertyKeySymbol();
}
