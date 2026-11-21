using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.Views;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Shell;

/// <summary>
/// Tests for the main window shell (E13-S01-T01, T02): areas that features fill, splitters, and the
/// collapsed and hidden panels.
/// </summary>
public sealed class ShellViewTests
{
    [Fact]
    public Task EveryArea_ShowsItsPlaceholderView() => RunInWindowAsync((window, viewModel) =>
    {
        var regions = viewModel.Regions;
        regions.Navigation = new PlaceholderViewModel();
        regions.Toolbar = new PlaceholderViewModel();
        regions.Browser = new PlaceholderViewModel();
        regions.Details = new PlaceholderViewModel();
        Dispatcher.UIThread.RunJobs();

        foreach (var area in new[] { "NavigationPanel", "ToolbarArea", "BrowserArea", "DetailsPanel" })
        {
            Assert.Single(Find<Control>(window, area).GetVisualDescendants().OfType<PlaceholderView>());
        }

        Assert.True(Find<Border>(window, "ToolbarArea").IsVisible);
    });

    [Fact]
    public Task ToolbarArea_TakesNoSpaceWhileEmpty() => RunInWindowAsync((window, _) =>
        Assert.False(Find<Border>(window, "ToolbarArea").IsVisible));

    [Fact]
    public Task Splitters_ResizeTheSidePanels() => RunInWindowAsync((window, viewModel) =>
    {
        Drag(window, Find<PanelSplitter>(window, "NavigationSplitter"), 60);
        Drag(window, Find<PanelSplitter>(window, "DetailsSplitter"), -100);

        Assert.Equal(300, viewModel.Layout.Navigation.Width);
        Assert.Equal(420, viewModel.Layout.Details.Width);
        Assert.Equal(300, Find<Panel>(window, "NavigationHost").Bounds.Width);
        Assert.Equal(420, Find<Panel>(window, "DetailsHost").Bounds.Width);
    });

    [Fact]
    public Task Splitter_KeepsTheBrowserAtItsMinimumWidth() => RunInWindowAsync((window, viewModel) =>
    {
        Drag(window, Find<PanelSplitter>(window, "NavigationSplitter"), 200);
        Drag(window, Find<PanelSplitter>(window, "DetailsSplitter"), -400);

        Assert.Equal(viewModel.Layout.Navigation.MaxWidth, viewModel.Layout.Navigation.Width);
        Assert.Equal(viewModel.Layout.Details.MaxWidth, viewModel.Layout.Details.Width);

        Assert.Equal(ShellLayout.BrowserMinWidth, Find<Border>(window, "BrowserArea").Bounds.Width);
    });

    [Fact]
    public Task Splitter_DraggedFarEnough_CollapsesThePanel() => RunInWindowAsync((window, viewModel) =>
    {
        Drag(window, Find<PanelSplitter>(window, "NavigationSplitter"), -200);

        Assert.True(viewModel.Layout.Navigation.IsCollapsed);
        Assert.Equal(66, Find<Panel>(window, "NavigationHost").Bounds.Width);

        Drag(window, Find<PanelSplitter>(window, "NavigationSplitter"), 100);

        Assert.True(viewModel.Layout.Navigation.IsExpanded);
    });

    [Fact]
    public Task Splitter_ResizesWithTheArrowKeys() => RunInWindowAsync((window, viewModel) =>
    {
        var splitter = Find<PanelSplitter>(window, "DetailsSplitter");
        splitter.Focus(NavigationMethod.Tab);

        splitter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });

        Assert.Equal(330, viewModel.Layout.Details.Width);
    });

    [Fact]
    public Task CollapsedPanels_ShowARailAndAStrip() => RunInWindowAsync((window, viewModel) =>
    {
        viewModel.Layout.Navigation.Collapse();
        viewModel.Layout.Details.Collapse();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(66, Find<Panel>(window, "NavigationHost").Bounds.Width);
        Assert.Equal(42, Find<Panel>(window, "DetailsHost").Bounds.Width);
        Assert.True(Find<Button>(window, "DetailsExpandButton").IsEffectivelyVisible);
    });

    [Fact]
    public Task HiddenPanels_LeaveAButtonToBringThemBack() => RunInWindowAsync((window, viewModel) =>
    {
        var showButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Command == viewModel.Commands[ShellCommands.ToggleNavigation]
                || button.Command == viewModel.Commands[ShellCommands.ToggleDetails])
            .ToList();
        Assert.All(showButtons, button => Assert.False(button.IsEffectivelyVisible));

        viewModel.Layout.Navigation.IsVisible = false;
        viewModel.Layout.Details.IsVisible = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Find<Panel>(window, "NavigationHost").IsVisible);
        Assert.False(Find<Panel>(window, "DetailsHost").IsVisible);
        Assert.Equal(2, showButtons.Count(button => button.IsEffectivelyVisible));

        showButtons.ForEach(button => button.Command!.Execute(null));
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.Layout.Navigation.IsVisible);
        Assert.True(viewModel.Layout.Details.IsVisible);
    });

    [Fact]
    public Task ResizingTheWindow_MovesBetweenTheThreeLayouts() => RunInWindowAsync((window, viewModel) =>
    {
        Assert.Equal(LayoutBreakpoint.Wide, viewModel.Layout.Breakpoint);
        Assert.Equal(240, Find<Panel>(window, "NavigationHost").Bounds.Width);
        Assert.True(Find<Panel>(window, "DetailsHost").IsEffectivelyVisible);

        window.Width = 1000;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(LayoutBreakpoint.Medium, viewModel.Layout.Breakpoint);
        Assert.Equal(66, Find<Panel>(window, "NavigationHost").Bounds.Width);
        Assert.False(Find<Panel>(window, "DetailsHost").IsVisible);

        window.Width = 760;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(LayoutBreakpoint.Narrow, viewModel.Layout.Breakpoint);
        Assert.False(Find<Panel>(window, "NavigationHost").IsVisible);

        window.Width = 1280;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(240, Find<Panel>(window, "NavigationHost").Bounds.Width);
        Assert.True(Find<Panel>(window, "DetailsHost").IsEffectivelyVisible);
    });

    [Fact]
    public Task Drawer_OpensOverTheBrowser_WithoutResizingIt() => RunInWindowAsync((window, viewModel) =>
    {
        window.Width = 760;
        Dispatcher.UIThread.RunJobs();
        var browserWidth = Find<Border>(window, "BrowserArea").Bounds.Width;
        Assert.False(Find<Border>(window, "DrawerScrim").IsEffectivelyVisible);

        viewModel.Layout.Details.ToggleVisible();
        Dispatcher.UIThread.RunJobs();

        var drawer = Find<Panel>(window, "DetailsHost");
        Assert.True(drawer.IsEffectivelyVisible);
        Assert.Equal(viewModel.Layout.Details.Width, drawer.Bounds.Width);
        Assert.True(Find<Border>(window, "DrawerScrim").IsEffectivelyVisible);
        Assert.Equal(browserWidth, Find<Border>(window, "BrowserArea").Bounds.Width);
        Assert.Equal(window.Bounds.Width, drawer.TranslatePoint(new Point(drawer.Bounds.Width, 0), window)!.Value.X);
    });

    [Fact]
    public Task Drawer_ClosesOnAScrimClick_AndOnEscape() => RunInWindowAsync((window, viewModel) =>
    {
        window.Width = 760;
        Dispatcher.UIThread.RunJobs();
        viewModel.Layout.Navigation.Expand();
        Dispatcher.UIThread.RunJobs();
        var scrim = Find<Border>(window, "DrawerScrim");

        var point = scrim.TranslatePoint(new Point(scrim.Bounds.Width - 5, scrim.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.Layout.IsAnyDrawerOpen);

        viewModel.Layout.Details.Expand();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.Layout.IsAnyDrawerOpen);
        Assert.False(Find<Panel>(window, "DetailsHost").IsVisible);
    });

    [Fact]
    public Task OpenButtons_ShowForClosedDrawers() => RunInWindowAsync((window, viewModel) =>
    {
        var buttons = window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Command == viewModel.Commands[ShellCommands.ToggleNavigation]
                || button.Command == viewModel.Commands[ShellCommands.ToggleDetails])
            .ToList();
        Assert.All(buttons, button => Assert.False(button.IsEffectivelyVisible));

        window.Width = 760;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, buttons.Count(button => button.IsEffectivelyVisible));
    });

    [Fact]
    public Task AtTheMinimumWidth_TheMenuFitsAndBothPanelsCanBeOpened() => RunInWindowAsync((window, viewModel) =>
    {
        window.Width = window.MinWidth;
        Dispatcher.UIThread.RunJobs();

        var menu = Find<Menu>(window, "MainMenu");
        Assert.True(menu.IsEffectivelyVisible);
        Assert.True(menu.Bounds.Width <= window.Bounds.Width);
        Assert.All(menu.GetVisualChildren().OfType<MenuItem>().Concat(menu.GetVisualDescendants().OfType<MenuItem>().Where(item => item.Parent == menu)),
            item => Assert.True(item.Bounds.Right <= window.Bounds.Width));

        // The commands in the menu open both drawers, and the panels' own buttons close them again.
        foreach (var (id, panel) in new[] { (ShellCommands.ToggleNavigation, viewModel.Layout.Navigation), (ShellCommands.ToggleDetails, viewModel.Layout.Details) })
        {
            ((System.Windows.Input.ICommand)viewModel.Commands[id]).Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(panel.IsExpanded);

            ((System.Windows.Input.ICommand)viewModel.Commands[id]).Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(panel.IsShown);
        }
    });

    [Fact]
    public Task F6_MovesTheFocusThroughTheAreasInOrder_AndWrapsAround() => RunInWindowAsync((window, viewModel) =>
    {
        FillRegions(viewModel);
        var visited = new List<string?>();

        for (var i = 0; i < 5; i++)
        {
            PressF6(window, RawInputModifiers.None);
            visited.Add(FocusedArea(window));
        }

        Assert.Equal(["NavigationHost", "ToolbarArea", "BrowserArea", "DetailsHost", "NavigationHost"], visited);
    });

    [Fact]
    public Task ShiftF6_MovesTheFocusBackwards_StartingAtTheLastArea() => RunInWindowAsync((window, viewModel) =>
    {
        FillRegions(viewModel);
        var visited = new List<string?>();

        for (var i = 0; i < 3; i++)
        {
            PressF6(window, RawInputModifiers.Shift);
            visited.Add(FocusedArea(window));
        }

        Assert.Equal(["DetailsHost", "BrowserArea", "ToolbarArea"], visited);
    });

    [Fact]
    public Task F6_SkipsAHiddenPanel() => RunInWindowAsync((window, viewModel) =>
    {
        FillRegions(viewModel);
        viewModel.Layout.Details.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        var visited = new List<string?>();

        for (var i = 0; i < 4; i++)
        {
            PressF6(window, RawInputModifiers.None);
            visited.Add(FocusedArea(window));
        }

        Assert.Equal(["NavigationHost", "ToolbarArea", "BrowserArea", "NavigationHost"], visited);
    });

    [Fact]
    public Task F6_ReturnsToTheControlThatHadTheFocusInThatArea() => RunInWindowAsync((window, viewModel) =>
    {
        FillRegions(viewModel);
        var browser = Find<Border>(window, "BrowserArea").GetVisualDescendants().OfType<PlaceholderView>().Single();
        browser.Focus(NavigationMethod.Tab);
        PressF6(window, RawInputModifiers.None);
        Assert.Equal("DetailsHost", FocusedArea(window));

        PressF6(window, RawInputModifiers.Shift);

        Assert.Same(browser, window.FocusManager!.GetFocusedElement());
    });

    [Fact]
    public Task Drawer_TakesTheFocusWhenItOpens_KeepsTabInside_AndGivesTheFocusBackOnEscape() => RunInWindowAsync((window, viewModel) =>
    {
        FillRegions(viewModel);
        window.Width = 760;
        Dispatcher.UIThread.RunJobs();
        var opener = Find<Border>(window, "BrowserArea").GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible);
        opener.Focus(NavigationMethod.Tab);

        viewModel.Layout.Details.ToggleVisible();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("DetailsHost", FocusedArea(window));
        for (var i = 0; i < 6; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Assert.Equal("DetailsHost", FocusedArea(window));
        }

        PressF6(window, RawInputModifiers.None);
        Assert.Equal("DetailsHost", FocusedArea(window));

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.Layout.IsAnyDrawerOpen);
        Assert.Equal("BrowserArea", FocusedArea(window));
    });

    [Fact]
    public Task FocusRing_ComesFromTheFocusTokenInBothThemes() => RunInWindowAsync((window, viewModel) =>
    {
        var button = Find<Border>(window, "NavigationPanel").GetVisualDescendants().OfType<Button>().First();

        foreach (var (variant, expected) in new[] { (Avalonia.Styling.ThemeVariant.Dark, Colors.White), (Avalonia.Styling.ThemeVariant.Light, Color.Parse("#1A1A1A")) })
        {
            var ring = Assert.IsType<Border>(button.FocusAdorner!.Build());
            var host = new Window { Content = ring, RequestedThemeVariant = variant };
            host.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(ring.BorderBrush).Color);
            Assert.Equal(2, ring.BorderThickness.Left);
            host.Close();
        }
    });

    private static void FillRegions(MainWindowViewModel viewModel)
    {
        viewModel.Regions.Navigation = new PlaceholderViewModel();
        viewModel.Regions.Toolbar = new PlaceholderViewModel();
        viewModel.Regions.Browser = new PlaceholderViewModel();
        viewModel.Regions.Details = new PlaceholderViewModel();
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressF6(Window window, RawInputModifiers modifiers)
    {
        window.KeyPressQwerty(PhysicalKey.F6, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static string? FocusedArea(Window window)
    {
        string[] areas = ["NavigationHost", "ToolbarArea", "BrowserArea", "DetailsHost"];
        for (var node = window.FocusManager!.GetFocusedElement() as Visual; node is not null; node = node.GetVisualParent())
        {
            if ((node as Control)?.Name is { } name && areas.Contains(name))
            {
                return name;
            }
        }

        return null;
    }

    private static Task RunInWindowAsync(Action<Window, MainWindowViewModel> test) => TestApp.RunAsync(() =>
    {
        using var services = new ServiceCollection()
            .AddSingleton<IAppPaths>(new FakeAppPaths())
            .AddSingleton<IFolderLauncher>(new FakeFolderLauncher())
            .AddSingleton<ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<ISettingsStore>(new InMemorySettingsStore())
            .AddSingleton<Infrastructure.Abstractions.Platform.IPlatformServices>(new Commands.FakePlatformServices())
            .AddSingleton<Infrastructure.Abstractions.Jobs.IJobProgress>(new FakeJobProgress())
            .AddSingleton<Infrastructure.Abstractions.Libraries.ILibrarySession>(new Browser.FakeBrowserSession(open: false))
            .AddUseCases()
            .AddUi()
            .BuildServiceProvider();
        var locator = services.GetRequiredService<ViewLocator>().Register<PlaceholderViewModel, PlaceholderView>();
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.DataTemplates.Add(locator);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            test(window, viewModel);
        }
        finally
        {
            window.Close();
        }
    });

    private static T Find<T>(Window window, string name)
        where T : Control
        => window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Drag(Window window, Control splitter, double deltaX)
    {
        var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(deltaX / 2, 0));
        window.MouseMove(start + new Vector(deltaX, 0));
        window.MouseUp(start + new Vector(deltaX, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class PlaceholderViewModel : ViewModelBase
    {
    }

    private sealed class PlaceholderView : Border
    {
        public PlaceholderView() => Focusable = true;
    }
}
