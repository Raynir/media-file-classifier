using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// One side panel of the main window (E13-S01): shown or hidden, expanded or collapsed to its icon rail or
/// strip, and resizable between a minimum and a maximum width. Hiding a panel keeps its collapsed state and
/// width, so showing it again brings it back as it was.
/// </summary>
public sealed partial class SidePanel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SidePanel"/> class with its default layout.
    /// </summary>
    /// <param name="defaultWidth">Width when expanded, before the user resizes it.</param>
    /// <param name="minWidth">Narrowest expanded width.</param>
    /// <param name="maxWidth">Widest expanded width.</param>
    public SidePanel(double defaultWidth, double minWidth, double maxWidth)
    {
        DefaultWidth = defaultWidth;
        MinWidth = minWidth;
        MaxWidth = maxWidth;
        Width = defaultWidth;
    }

    /// <summary>
    /// Gets the width when expanded, before the user resizes it.
    /// </summary>
    public double DefaultWidth { get; }

    /// <summary>
    /// Gets the narrowest expanded width.
    /// </summary>
    public double MinWidth { get; }

    /// <summary>
    /// Gets the widest expanded width.
    /// </summary>
    public double MaxWidth { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the panel is shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    public partial bool IsVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the panel is collapsed to its icon rail or strip.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    public partial bool IsCollapsed { get; set; }

    /// <summary>
    /// Gets or sets the width when expanded, between <see cref="MinWidth"/> and <see cref="MaxWidth"/>.
    /// </summary>
    [ObservableProperty]
    public partial double Width { get; set; }

    /// <summary>
    /// Gets a value indicating whether the panel is shown at full width.
    /// </summary>
    public bool IsExpanded => IsVisible && !IsCollapsed;

    /// <summary>
    /// Shows a hidden panel as it was, or hides a shown one.
    /// </summary>
    public void ToggleVisible() => IsVisible = !IsVisible;

    /// <summary>
    /// Collapses an expanded panel, or expands a collapsed one. A hidden panel is shown expanded.
    /// </summary>
    public void ToggleCollapsed() => Show(collapsed: IsVisible && !IsCollapsed);

    /// <summary>
    /// Shows the panel expanded.
    /// </summary>
    public void Expand() => Show(collapsed: false);

    /// <summary>
    /// Shows the panel collapsed.
    /// </summary>
    public void Collapse() => Show(collapsed: true);

    /// <summary>
    /// Resizes the panel by dragging its splitter. Dragging below half the minimum width collapses the panel,
    /// and dragging a collapsed panel out past that point expands it again.
    /// </summary>
    /// <param name="requested">The width the pointer asks for.</param>
    /// <param name="available">The widest the panel can be without squeezing the media browser below its minimum.</param>
    public void ResizeTo(double requested, double available)
    {
        if (requested < MinWidth / 2)
        {
            IsCollapsed = true;
            return;
        }

        IsCollapsed = false;
        Width = Math.Clamp(requested, MinWidth, Math.Max(MinWidth, Math.Min(MaxWidth, available)));
    }

    /// <summary>
    /// Resizes the panel by a step, for the keyboard. Growing a collapsed panel expands it at its last width;
    /// shrinking stops at the minimum width.
    /// </summary>
    /// <param name="delta">Change in width; negative to shrink.</param>
    /// <param name="available">The widest the panel can be without squeezing the media browser below its minimum.</param>
    public void ResizeBy(double delta, double available)
    {
        if (IsCollapsed)
        {
            IsCollapsed = delta <= 0;
            return;
        }

        ResizeTo(Math.Max(MinWidth, Width + delta), available);
    }

    /// <summary>
    /// Restores the default layout: shown, expanded, at the default width.
    /// </summary>
    public void Reset() => Apply(new PanelLayout(IsVisible: true, IsCollapsed: false, DefaultWidth));

    /// <summary>
    /// Gets the layout to save.
    /// </summary>
    /// <returns>The panel's layout.</returns>
    public PanelLayout ToLayout() => new(IsVisible, IsCollapsed, Width);

    /// <summary>
    /// Restores a saved layout. A width out of range, e.g. from an older version, is brought into range.
    /// </summary>
    /// <param name="layout">The saved layout.</param>
    public void Apply(PanelLayout layout)
    {
        IsVisible = layout.IsVisible;
        IsCollapsed = layout.IsCollapsed;
        Width = double.IsFinite(layout.Width) ? Math.Clamp(layout.Width, MinWidth, MaxWidth) : DefaultWidth;
    }

    private void Show(bool collapsed)
    {
        IsCollapsed = collapsed;
        IsVisible = true;
    }
}
