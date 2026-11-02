namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// The focus contexts a shortcut can belong to (E16-S01). Views mark their root with
/// <c>sc:Shortcuts.Scope="browser"</c>; a key press tries the scopes around the focused element, innermost
/// first, and then <see cref="Global"/>. So the same key can do different things in different scopes.
/// </summary>
public static class ShortcutScopes
{
    /// <summary>Anywhere in the window.</summary>
    public const string Global = "global";

    /// <summary>The media browser: grid and list (E14).</summary>
    public const string Browser = "browser";

    /// <summary>The metadata editor (E07).</summary>
    public const string Editor = "editor";

    /// <summary>Review mode (E08).</summary>
    public const string Review = "review";

    /// <summary>The full-screen viewer (E14).</summary>
    public const string Viewer = "viewer";

    /// <summary>
    /// Gets every scope, in the order the shortcut reference lists them.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Global, Browser, Editor, Review, Viewer];

    /// <summary>
    /// Gets the resource key of a scope's name in the shortcut reference, e.g. <c>Shortcuts_Scope_Global</c>.
    /// </summary>
    /// <param name="scope">One of the scopes.</param>
    /// <returns>The resource key.</returns>
    public static string NameKey(string scope) => "Shortcuts_Scope_" + char.ToUpperInvariant(scope[0]) + scope[1..];
}
