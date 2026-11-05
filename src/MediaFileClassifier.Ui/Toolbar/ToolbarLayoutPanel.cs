using Avalonia;
using Avalonia.Controls;

namespace MediaFileClassifier.Ui.Toolbar;

/// <summary>
/// How <see cref="ToolbarLayoutPanel"/> sizes a child.
/// </summary>
public enum ToolbarLayoutRole
{
    /// <summary>
    /// The child takes the width it asks for.
    /// </summary>
    Fixed,

    /// <summary>
    /// The child takes the width left over, and at least its <c>MinWidth</c>. One child at most.
    /// </summary>
    Fill,

    /// <summary>
    /// The child is offered the width left after the fixed children and the fill child's minimum, and fits into
    /// it by moving content into an overflow menu (<see cref="ToolbarActionPanel"/>).
    /// </summary>
    Overflow,
}

/// <summary>
/// Lays out the toolbar's children in one row, in their order (E13-S03-T01). Unlike a grid with auto columns, it
/// offers the overflow child only the width that is really left, so the actions move into the overflow menu
/// instead of pushing the slots out of the window. A child with no width gets no spacing either.
/// </summary>
public sealed class ToolbarLayoutPanel : Panel
{
    /// <summary>
    /// Defines the <see cref="Spacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ToolbarLayoutPanel, double>(nameof(Spacing));

    /// <summary>
    /// Defines the Role attached property.
    /// </summary>
    public static readonly AttachedProperty<ToolbarLayoutRole> RoleProperty =
        AvaloniaProperty.RegisterAttached<ToolbarLayoutPanel, Control, ToolbarLayoutRole>("Role");

    static ToolbarLayoutPanel()
    {
        AffectsMeasure<ToolbarLayoutPanel>(SpacingProperty);
        AffectsParentMeasure<ToolbarLayoutPanel>(RoleProperty);
    }

    /// <summary>
    /// Gets or sets the space between two children.
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Gets how the panel sizes a child.
    /// </summary>
    /// <param name="control">The child.</param>
    /// <returns>The role.</returns>
    public static ToolbarLayoutRole GetRole(Control control) => control.GetValue(RoleProperty);

    /// <summary>
    /// Sets how the panel sizes a child.
    /// </summary>
    /// <param name="control">The child.</param>
    /// <param name="value">The role.</param>
    public static void SetRole(Control control, ToolbarLayoutRole value) => control.SetValue(RoleProperty, value);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        var used = 0.0;
        var gaps = 0;
        Control? fill = null;
        foreach (var child in Children.Where(child => child.IsVisible))
        {
            switch (GetRole(child))
            {
                case ToolbarLayoutRole.Fill:
                    fill = child;
                    used += child.MinWidth;
                    gaps++;
                    break;
                case ToolbarLayoutRole.Fixed:
                    child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                    used += child.DesiredSize.Width;
                    gaps += child.DesiredSize.Width > 0 ? 1 : 0;
                    height = Math.Max(height, child.DesiredSize.Height);
                    break;
            }
        }

        foreach (var child in Children.Where(child => child.IsVisible && GetRole(child) == ToolbarLayoutRole.Overflow))
        {
            // Offer the width left once the gap before this child is taken off too.
            var left = Math.Max(0, availableSize.Width - used - (gaps * Spacing));
            child.Measure(new Size(left, availableSize.Height));
            used += child.DesiredSize.Width;
            gaps += child.DesiredSize.Width > 0 ? 1 : 0;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var spacing = Math.Max(0, gaps - 1) * Spacing;
        if (fill is not null)
        {
            var rest = availableSize.Width - (used - fill.MinWidth) - spacing;
            fill.Measure(new Size(Math.Max(fill.MinWidth, rest), availableSize.Height));
            height = Math.Max(height, fill.DesiredSize.Height);
        }

        var width = double.IsInfinity(availableSize.Width) ? used + spacing : availableSize.Width;
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Where(child => child.IsVisible).ToList();
        var sized = visible.Where(child => GetRole(child) == ToolbarLayoutRole.Fill || child.DesiredSize.Width > 0).ToList();
        var others = sized.Where(child => GetRole(child) != ToolbarLayoutRole.Fill).Sum(child => child.DesiredSize.Width);
        var spacing = Math.Max(0, sized.Count - 1) * Spacing;
        var x = 0.0;
        foreach (var child in visible)
        {
            if (!sized.Contains(child))
            {
                child.Arrange(new Rect(x, 0, 0, finalSize.Height));
                continue;
            }

            var width = GetRole(child) == ToolbarLayoutRole.Fill
                ? Math.Max(child.MinWidth, finalSize.Width - others - spacing)
                : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }

        return finalSize;
    }
}
