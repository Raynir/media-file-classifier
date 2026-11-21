using MediaFileClassifier.Ui.Shell;

namespace MediaFileClassifier.Ui.Tests.Shell;

/// <summary>
/// Tests for the layout breakpoints and the panel modes they switch (E13-S07-T01, T02).
/// </summary>
public sealed class BreakpointTests
{
    private readonly ShellLayout layout = new();

    [Theory]
    [InlineData(1920, LayoutBreakpoint.Wide)]
    [InlineData(1100, LayoutBreakpoint.Wide)]
    [InlineData(1099.5, LayoutBreakpoint.Medium)]
    [InlineData(800, LayoutBreakpoint.Medium)]
    [InlineData(799.5, LayoutBreakpoint.Narrow)]
    [InlineData(720, LayoutBreakpoint.Narrow)]
    [InlineData(0, LayoutBreakpoint.Wide)]
    [InlineData(double.NaN, LayoutBreakpoint.Wide)]
    public void For_PicksTheLayoutOfTheWidth(double width, LayoutBreakpoint expected)
        => Assert.Equal(expected, LayoutBreakpoints.For(width));

    [Theory]
    [InlineData(1280, PanelMode.Docked, PanelMode.Docked)]
    [InlineData(900, PanelMode.Rail, PanelMode.Drawer)]
    [InlineData(720, PanelMode.Drawer, PanelMode.Drawer)]
    public void UpdateWidth_SetsThePanelModes(double width, PanelMode navigation, PanelMode details)
    {
        layout.UpdateWidth(width);

        Assert.Equal(navigation, layout.Navigation.Mode);
        Assert.Equal(details, layout.Details.Mode);
    }

    [Fact]
    public void WideLayout_ShowsBothPanelsDocked_AsSaved()
    {
        layout.UpdateWidth(1280);

        Assert.True(layout.Navigation is { IsShown: true, IsExpanded: true, IsDrawer: false, NeedsOpenButton: false, IsResizable: true });
        Assert.True(layout.Details is { IsShown: true, IsExpanded: true, IsDrawer: false, NeedsOpenButton: false });
    }

    [Fact]
    public void MediumLayout_ShowsTheNavigationRail_AndClosesTheDetailsDrawer()
    {
        layout.UpdateWidth(900);

        Assert.True(layout.Navigation is { IsShown: true, IsRail: true, IsExpanded: false, IsResizable: false });
        Assert.True(layout.Details is { IsShown: false, IsDrawer: true, NeedsOpenButton: true });
    }

    [Fact]
    public void NarrowLayout_ClosesBothDrawers_AndOffersToOpenThem()
    {
        layout.UpdateWidth(720);

        Assert.True(layout.Navigation is { IsShown: false, IsDrawer: true, NeedsOpenButton: true });
        Assert.True(layout.Details is { IsShown: false, IsDrawer: true, NeedsOpenButton: true });
        Assert.False(layout.IsAnyDrawerOpen);
    }

    [Fact]
    public void ExpandingTheRail_OpensTheNavigationDrawerOverIt_AndKeepsTheRailColumn()
    {
        layout.UpdateWidth(900);

        layout.Navigation.ToggleCollapsed();

        Assert.True(layout.Navigation is { IsExpanded: true, IsDrawer: true, IsRail: false, KeepsRailColumn: true });
        Assert.True(layout.IsAnyDrawerOpen);

        layout.Navigation.ToggleCollapsed();

        Assert.True(layout.Navigation is { IsRail: true, IsDrawer: false, KeepsRailColumn: false });
    }

    [Fact]
    public void OpeningADrawer_ClosesTheOtherOne()
    {
        layout.UpdateWidth(720);
        layout.Navigation.Expand();

        layout.Details.Expand();

        Assert.True(layout.Details.IsExpanded);
        Assert.False(layout.Navigation.IsExpanded);
    }

    [Fact]
    public void DrawerCommands_OpenAndCloseTheDrawer_WithoutTouchingTheSavedState()
    {
        layout.UpdateWidth(720);
        var saved = layout.ToSettings();

        layout.Details.ToggleVisible();
        Assert.True(layout.Details.IsExpanded);

        layout.Details.Collapse();
        Assert.False(layout.Details.IsShown);

        layout.Details.Expand();
        layout.Details.Hide();
        Assert.False(layout.Details.IsShown);

        layout.Navigation.ToggleCollapsed();
        Assert.True(layout.Navigation.IsExpanded);

        Assert.Equal(saved, layout.ToSettings());
    }

    [Fact]
    public void CloseDrawers_ClosesTheOpenDrawer()
    {
        layout.UpdateWidth(720);
        layout.Details.Expand();

        layout.CloseDrawers();

        Assert.False(layout.IsAnyDrawerOpen);
        Assert.False(layout.Details.IsShown);
    }

    [Fact]
    public void ChangingTheBreakpoint_ClosesDrawers_AndWideRestoresTheSavedLayout()
    {
        layout.Details.ResizeTo(400, double.PositiveInfinity);
        layout.Navigation.Collapse();
        var saved = layout.ToSettings();

        layout.UpdateWidth(720);
        layout.Details.Expand();
        layout.UpdateWidth(900);

        Assert.False(layout.IsAnyDrawerOpen);

        layout.UpdateWidth(1400);

        Assert.True(layout.Navigation is { IsRail: true, IsDrawer: false });
        Assert.True(layout.Details is { IsExpanded: true, Width: 400 });
        Assert.Equal(saved, layout.ToSettings());
    }

    [Fact]
    public void UpdateWidth_WithinTheSameBreakpoint_KeepsAnOpenDrawer()
    {
        layout.UpdateWidth(720);
        layout.Details.Expand();

        layout.UpdateWidth(760);

        Assert.True(layout.Details.IsExpanded);
    }

    [Fact]
    public void ResizingADrawerOrRail_DoesNothing()
    {
        layout.UpdateWidth(900);
        var width = layout.Navigation.Width;

        layout.Navigation.ResizeTo(300, double.PositiveInfinity);
        layout.Navigation.ResizeBy(10, double.PositiveInfinity);

        Assert.Equal(width, layout.Navigation.Width);
    }

    [Fact]
    public void HiddenPanel_InTheWideLayout_NeedsAnOpenButton()
    {
        layout.UpdateWidth(1280);

        layout.Details.Hide();

        Assert.True(layout.Details is { IsShown: false, NeedsOpenButton: true });
    }

    [Fact]
    public void Hide_OnARailWithItsDrawerOpen_ClosesTheDrawerAndHidesTheRail()
    {
        layout.UpdateWidth(900);
        layout.Navigation.Expand();

        layout.Navigation.Hide();

        Assert.True(layout.Navigation is { IsShown: false, IsVisible: false, NeedsOpenButton: true });
    }

    [Fact]
    public void OpeningADrawer_RaisesTheStateChange_ButIsNotAChangeToSave()
    {
        layout.UpdateWidth(720);
        var saved = 0;
        var state = 0;
        layout.Changed += (_, _) => saved++;
        layout.StateChanged += (_, _) => state++;

        layout.Details.Expand();
        layout.Details.Hide();

        Assert.Equal(0, saved);
        Assert.True(state > 0);

        layout.UpdateWidth(1280);
        layout.Details.ResizeTo(400, double.PositiveInfinity);

        Assert.True(saved > 0);
    }
}
