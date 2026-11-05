using Avalonia;
using Avalonia.Controls;

namespace MediaFileClassifier.Ui.Toolbar;

/// <summary>
/// Lays out the toolbar's action buttons and the More button (E13-S03-T03). It's the items panel of a list of
/// <see cref="ToolbarAction"/> items followed by the <see cref="ToolbarOverflowMenu"/>. When the buttons don't
/// fit the width offered, it marks actions <see cref="ToolbarAction.IsOverflowed"/>, as
/// <see cref="ToolbarOverflow.Fit"/> decides, and the view hides them and lists them in the menu.
/// </summary>
public sealed class ToolbarActionPanel : Panel
{
    /// <summary>
    /// Defines the <see cref="Spacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ToolbarActionPanel, double>(nameof(Spacing));

    /// <summary>
    /// Defines the <see cref="OverflowButtonWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> OverflowButtonWidthProperty =
        AvaloniaProperty.Register<ToolbarActionPanel, double>(nameof(OverflowButtonWidth));

    // A hidden button measures as zero wide, so each button's width is remembered from when it was last shown.
    private readonly Dictionary<Control, double> widths = [];

    static ToolbarActionPanel()
    {
        AffectsMeasure<ToolbarActionPanel>(SpacingProperty, OverflowButtonWidthProperty);
    }

    /// <summary>
    /// Gets or sets the space between two buttons.
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the More button, which the panel keeps free while any action is in the menu.
    /// </summary>
    public double OverflowButtonWidth
    {
        get => GetValue(OverflowButtonWidthProperty);
        set => SetValue(OverflowButtonWidthProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var gone in widths.Keys.Where(child => !Children.Contains(child)).ToList())
        {
            widths.Remove(gone);
        }

        var height = 0.0;
        var actions = new List<(Control Child, ToolbarAction Action)>();
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            if (child.DataContext is ToolbarAction action)
            {
                if (child.DesiredSize.Width > 0)
                {
                    widths[child] = child.DesiredSize.Width;
                }

                actions.Add((child, action));
            }
        }

        var overflowed = ToolbarOverflow.Fit(
            availableSize.Width,
            [.. actions.Select(item => widths.GetValueOrDefault(item.Child))],
            [.. actions.Select(item => item.Action.Priority)],
            OverflowButtonWidth,
            Spacing);
        for (var index = 0; index < actions.Count; index++)
        {
            actions[index].Action.IsOverflowed = overflowed[index];
        }

        var shown = actions.Where((_, index) => !overflowed[index]).Select(item => widths.GetValueOrDefault(item.Child)).ToList();
        var buttons = shown.Count + (overflowed.Any(isOverflowed => isOverflowed) ? 1 : 0);
        var width = shown.Sum()
            + (buttons > shown.Count ? OverflowButtonWidth : 0)
            + (Math.Max(0, buttons - 1) * Spacing);
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var anyOverflowed = false;
        Control? more = null;
        foreach (var child in Children)
        {
            if (child.DataContext is not ToolbarAction action)
            {
                more = child;
                continue;
            }

            if (action.IsOverflowed)
            {
                anyOverflowed = true;
                child.Arrange(default);
                continue;
            }

            var width = widths.GetValueOrDefault(child);
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }

        more?.Arrange(anyOverflowed ? new Rect(x, 0, OverflowButtonWidth, finalSize.Height) : default);
        return finalSize;
    }
}
