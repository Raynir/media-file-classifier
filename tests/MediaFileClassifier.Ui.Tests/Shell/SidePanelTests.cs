using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Shell;

namespace MediaFileClassifier.Ui.Tests.Shell;

/// <summary>
/// Tests for a side panel's states and sizes (E13-S01-T01, T02).
/// </summary>
public sealed class SidePanelTests
{
    private readonly SidePanel panel = new(defaultWidth: 240, minWidth: 180, maxWidth: 400);

    [Fact]
    public void ToggleVisible_KeepsTheCollapsedStateAndWidth()
    {
        panel.ResizeTo(300, double.PositiveInfinity);
        panel.Collapse();

        panel.ToggleVisible();
        Assert.False(panel.IsVisible);

        panel.ToggleVisible();
        Assert.True(panel is { IsVisible: true, IsCollapsed: true, Width: 300 });
    }

    [Fact]
    public void ToggleCollapsed_FlipsAndShowsAHiddenPanelExpanded()
    {
        panel.ToggleCollapsed();
        Assert.True(panel.IsCollapsed);

        panel.ToggleCollapsed();
        Assert.True(panel.IsExpanded);

        panel.Collapse();
        panel.IsVisible = false;
        panel.ToggleCollapsed();
        Assert.True(panel.IsExpanded);
    }

    [Theory]
    [InlineData(250, 250)]
    [InlineData(100, 180)]
    [InlineData(1000, 400)]
    public void ResizeTo_StaysBetweenTheLimits(double requested, double expected)
    {
        panel.ResizeTo(requested, double.PositiveInfinity);

        Assert.Equal(expected, panel.Width);
        Assert.False(panel.IsCollapsed);
    }

    [Fact]
    public void ResizeTo_NeverSqueezesTheBrowserBelowItsMinimum()
    {
        panel.ResizeTo(380, available: 300);
        Assert.Equal(300, panel.Width);

        // The minimum width still wins over a cramped window.
        panel.ResizeTo(380, available: 100);
        Assert.Equal(180, panel.Width);
    }

    [Fact]
    public void ResizeTo_BelowHalfTheMinimum_CollapsesAndKeepsTheWidth()
    {
        panel.ResizeTo(300, double.PositiveInfinity);

        panel.ResizeTo(89, double.PositiveInfinity);

        Assert.True(panel.IsCollapsed);
        Assert.Equal(300, panel.Width);
    }

    [Fact]
    public void ResizeTo_DraggingACollapsedPanelOut_ExpandsIt()
    {
        panel.Collapse();

        panel.ResizeTo(80, double.PositiveInfinity);
        Assert.True(panel.IsCollapsed);

        panel.ResizeTo(95, double.PositiveInfinity);
        Assert.True(panel.IsExpanded);
        Assert.Equal(180, panel.Width);
    }

    [Fact]
    public void ResizeBy_StepsWithoutCollapsing()
    {
        panel.ResizeBy(-100, double.PositiveInfinity);
        Assert.Equal(180, panel.Width);
        Assert.False(panel.IsCollapsed);

        panel.ResizeBy(10, double.PositiveInfinity);
        Assert.Equal(190, panel.Width);
    }

    [Fact]
    public void ResizeBy_GrowingACollapsedPanel_ExpandsItAtItsWidth()
    {
        panel.ResizeTo(300, double.PositiveInfinity);
        panel.Collapse();

        panel.ResizeBy(-10, double.PositiveInfinity);
        Assert.True(panel.IsCollapsed);

        panel.ResizeBy(10, double.PositiveInfinity);
        Assert.True(panel is { IsExpanded: true, Width: 300 });
    }

    [Theory]
    [InlineData(1000, 400)]
    [InlineData(10, 180)]
    [InlineData(double.NaN, 240)]
    public void Apply_BringsASavedWidthIntoRange(double saved, double expected)
    {
        panel.Apply(new PanelLayout(IsVisible: false, IsCollapsed: true, saved));

        Assert.Equal(new PanelLayout(false, true, expected), panel.ToLayout());
    }

    [Fact]
    public void Reset_RestoresTheDefaults()
    {
        panel.Apply(new PanelLayout(IsVisible: false, IsCollapsed: true, 350));

        panel.Reset();

        Assert.Equal(new PanelLayout(true, false, 240), panel.ToLayout());
    }
}
