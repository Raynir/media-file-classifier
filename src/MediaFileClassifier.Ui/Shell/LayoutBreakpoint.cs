namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// The main window's layout for its width (E13-S07, spec 2.15).
/// </summary>
public enum LayoutBreakpoint
{
    /// <summary>All panels are docked.</summary>
    Wide,

    /// <summary>The navigation panel is an icon rail, and the details panel is a drawer.</summary>
    Medium,

    /// <summary>Both side panels are drawers.</summary>
    Narrow,
}

/// <summary>
/// The widths where the layout changes, in device-independent pixels.
/// </summary>
public static class LayoutBreakpoints
{
    /// <summary>The narrowest width of the <see cref="LayoutBreakpoint.Wide"/> layout.</summary>
    public const double WideMinWidth = 1100;

    /// <summary>The narrowest width of the <see cref="LayoutBreakpoint.Medium"/> layout.</summary>
    public const double MediumMinWidth = 800;

    /// <summary>
    /// Gets the layout for a width. A width that isn't known yet, such as before the first layout pass, is wide.
    /// </summary>
    /// <param name="width">The window's width.</param>
    /// <returns>The breakpoint.</returns>
    public static LayoutBreakpoint For(double width) => width switch
    {
        _ when !double.IsFinite(width) || width <= 0 => LayoutBreakpoint.Wide,
        >= WideMinWidth => LayoutBreakpoint.Wide,
        >= MediumMinWidth => LayoutBreakpoint.Medium,
        _ => LayoutBreakpoint.Narrow,
    };
}

/// <summary>
/// How a side panel is shown at the current breakpoint.
/// </summary>
public enum PanelMode
{
    /// <summary>In its column, shown, collapsed or hidden as the user chose.</summary>
    Docked,

    /// <summary>In its column as an icon rail. Expanding it opens the panel as a drawer over the browser.</summary>
    Rail,

    /// <summary>Over the browser, opened and closed on demand.</summary>
    Drawer,
}
