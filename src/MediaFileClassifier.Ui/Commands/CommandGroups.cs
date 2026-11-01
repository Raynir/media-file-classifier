namespace MediaFileClassifier.Ui.Commands;

/// <summary>
/// The main menu groups, in menu order (E13-S05).
/// </summary>
public static class CommandGroups
{
    /// <summary>Library: create, open, rescan and close libraries, exit.</summary>
    public const string Library = "library";

    /// <summary>Edit: undo, select all, edit metadata.</summary>
    public const string Edit = "edit";

    /// <summary>View: panels, layout, theme.</summary>
    public const string View = "view";

    /// <summary>Item: actions on the selected items.</summary>
    public const string Item = "item";

    /// <summary>Tools: settings and developer tools.</summary>
    public const string Tools = "tools";

    /// <summary>Help: logs, shortcuts, about.</summary>
    public const string Help = "help";

    /// <summary>
    /// Gets every group in menu order.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Library, Edit, View, Item, Tools, Help];

    /// <summary>
    /// Gets the resource key of a group's menu header, e.g. <c>Menu_Library</c>.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <returns>The resource key.</returns>
    public static string HeaderKey(string group) => "Menu_" + char.ToUpperInvariant(group[0]) + group[1..];
}
