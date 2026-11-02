using Avalonia;
using Avalonia.Controls;

namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// Attached properties for shortcuts (E16-S01). Mark a view's root with <c>sc:Shortcuts.Scope="browser"</c>
/// (one of <see cref="ShortcutScopes"/>) to make that scope's shortcuts work while focus is inside it.
/// </summary>
public sealed class Shortcuts
{
    /// <summary>
    /// The scope of the shortcuts that work while focus is inside the element.
    /// </summary>
    public static readonly AttachedProperty<string?> ScopeProperty =
        AvaloniaProperty.RegisterAttached<Shortcuts, Control, string?>("Scope");

    private Shortcuts()
    {
    }

    /// <summary>
    /// Gets the element's shortcut scope.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The scope, or <c>null</c>.</returns>
    public static string? GetScope(Control element) => element.GetValue(ScopeProperty);

    /// <summary>
    /// Sets the element's shortcut scope.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="value">One of <see cref="ShortcutScopes"/>, or <c>null</c>.</param>
    public static void SetScope(Control element, string? value) => element.SetValue(ScopeProperty, value);
}
