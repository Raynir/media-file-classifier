using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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

    private static Task RunInWindowAsync(Action<Window, MainWindowViewModel> test) => TestApp.RunAsync(() =>
    {
        using var services = new ServiceCollection()
            .AddSingleton<IAppPaths>(new FakeAppPaths())
            .AddSingleton<IFolderLauncher>(new FakeFolderLauncher())
            .AddSingleton<ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<ISettingsStore>(new InMemorySettingsStore())
            .AddSingleton<Infrastructure.Abstractions.Platform.IPlatformServices>(new Commands.FakePlatformServices())
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
    }
}
