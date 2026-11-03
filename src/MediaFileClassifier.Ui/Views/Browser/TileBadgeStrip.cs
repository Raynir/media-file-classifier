using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Views.Browser;

/// <summary>
/// A tile's badge slot (E14-S01-T04). Every badge is a <c>StatusBadge</c> border styled by its kind, so a new badge
/// type needs no layout change. Most tiles have no badge, so the strip builds nothing for them: an items control per
/// tile cost more than the rest of the tile while scrolling.
/// </summary>
public sealed class TileBadgeStrip : StackPanel
{
    /// <summary>
    /// Defines the <see cref="Badges"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<TileBadge>?> BadgesProperty =
        AvaloniaProperty.Register<TileBadgeStrip, IReadOnlyList<TileBadge>?>(nameof(Badges));

    static TileBadgeStrip()
    {
        BadgesProperty.Changed.AddClassHandler<TileBadgeStrip>((strip, _) => strip.Update());
    }

    /// <summary>
    /// Gets or sets the badges to show.
    /// </summary>
    public IReadOnlyList<TileBadge>? Badges
    {
        get => GetValue(BadgesProperty);
        set => SetValue(BadgesProperty, value);
    }

    private void Update()
    {
        var badges = Badges ?? [];
        if (badges.Count == 0 && Children.Count == 0)
        {
            return;
        }

        Children.Clear();
        foreach (var badge in badges)
        {
            var border = new Border { Child = new Avalonia.Controls.Shapes.Path { Data = badge.Icon } };
            border.Classes.Add("StatusBadge");
            border.Classes.Add(badge.Kind);
            ToolTip.SetTip(border, badge.ToolTip);
            AutomationProperties.SetName(border, badge.Label);
            Children.Add(border);
        }
    }
}
