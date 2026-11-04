using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.Toolbar;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.Views;
using MediaFileClassifier.Ui.Views.Browser;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Toolbar;

/// <summary>
/// Tests for the toolbar in the main window (E13-S03): each slot shows the control a feature puts in it, in a
/// fixed order at every window width (T01), each action button runs its command and shows its shortcut (T02), and
/// actions that don't fit stay reachable through the More menu (T03).
/// </summary>
public sealed class ToolbarViewTests
{
    [Theory]
    [InlineData(1440)]
    [InlineData(1000)]
    [InlineData(720)]
    public Task Slots_HostTheirControls_InOrder_AtEveryWidth(double width) => RunInWindowAsync(width, (window, _) =>
    {
        var placeholders = window.GetVisualDescendants().OfType<PlaceholderView>().ToList();
        Assert.Equal(["search", "filter", "sort", "view"], placeholders.OrderBy(view => Left(view, window)).Select(view => view.Slot));
        Assert.All(placeholders, view => Assert.True(view.IsEffectivelyVisible && view.Bounds.Width > 0, view.Slot));

        // Nothing is pushed out of the toolbar.
        var toolbar = Toolbar(window);
        var right = placeholders.Max(view => Left(view, window) + view.Bounds.Width);
        Assert.True(right <= Left(toolbar, window) + toolbar.Bounds.Width, $"{right} > {toolbar.Bounds.Right}");
    });

    [Fact]
    public Task Search_FillsTheWidthTheOtherSlotsLeave() => RunInWindowAsync(1440, (window, _) =>
    {
        var search = window.GetVisualDescendants().OfType<PlaceholderView>().Single(view => view.Slot == "search");
        var filter = window.GetVisualDescendants().OfType<PlaceholderView>().Single(view => view.Slot == "filter");

        Assert.True(search.Bounds.Width > 160, $"search is {search.Bounds.Width} wide");
        Assert.Equal(Left(search, window) + search.Bounds.Width + 12, Left(filter, window));
        Assert.Equal(PlaceholderView.FixedWidth, filter.Bounds.Width);
    });

    [Fact]
    public Task EmptySlots_TakeNoSpace() => RunInWindowAsync(1440, (window, toolbar) =>
    {
        var sortLeft = Left(window.GetVisualDescendants().OfType<PlaceholderView>().Single(view => view.Slot == "sort"), window);
        toolbar.Filter = null;
        Dispatcher.UIThread.RunJobs();

        var placeholders = window.GetVisualDescendants().OfType<PlaceholderView>().Where(view => view.IsEffectivelyVisible).ToList();
        Assert.Equal(["search", "sort", "view"], placeholders.OrderBy(view => Left(view, window)).Select(view => view.Slot));
        Assert.Equal(sortLeft, Left(placeholders.Single(view => view.Slot == "sort"), window));
    });

    [Fact]
    public Task Toolbar_TakesNoSpaceWhileTheRegionIsEmpty() => RunInWindowAsync(1440, (window, _) =>
    {
        var area = window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ToolbarArea");
        Assert.True(area.IsVisible);

        ((MainWindowViewModel)window.DataContext!).Regions.Toolbar = null;
        Dispatcher.UIThread.RunJobs();

        Assert.False(area.IsVisible);
    });

    [Fact]
    public Task ViewSlot_HoldsTheThumbnailSizeSlider_WhichSetsTheGridsTileSize() => TestApp.RunAsync(() =>
    {
        var source = new FakeResultSource(10);
        var grid = new ThumbnailGridViewModel(
            source,
            new ThumbnailLoader(new FakeBrowserSession(), new FakeThumbnailCache(), new ThumbnailBitmapCache()),
            new FakeItemOpener(),
            new FakeJobSystem(),
            new FakeTileSizeSender(),
            new SelectionModel(() => source));
        var toolbar = new ToolbarViewModel([new BrowserToolbar(() => grid)]);
        var view = new ThumbnailSizeView { DataContext = Assert.IsType<ThumbnailSizeViewModel>(toolbar.View) };
        var window = new Window { Content = view, Width = 400, Height = 80 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        view.GetVisualDescendants().OfType<Slider>().Single().Value = 256;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(256, grid.TileSize);
        window.Close();
    });

    [Fact]
    public Task Actions_RunTheirCommands_AndShowTheirShortcuts() => RunInWindowAsync(1440, (window, _) =>
    {
        var viewModel = (MainWindowViewModel)window.DataContext!;
        var button = ActionButtons(window).Single(button => Label(button) == "Show or hide details panel");
        Assert.Equal("Show or hide details panel (Ctrl+2)", ToolTip.GetTip(button));
        Assert.Equal("Show or hide details panel", Avalonia.Automation.AutomationProperties.GetName(button));
        Assert.True(viewModel.Layout.Details.IsVisible);

        button.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.Layout.Details.IsVisible);
    });

    [Fact]
    public Task DisabledAction_SaysWhy() => RunInWindowAsync(1440, (window, _) =>
    {
        var button = ActionButtons(window).Single(button => Label(button) == TestCommands.DisabledLabel);

        Assert.False(button.IsEffectivelyEnabled);
        Assert.Equal(TestCommands.DisabledReason, ToolTip.GetTip(button));
        Assert.True(ToolTip.GetShowOnDisabled(button));
    });

    [Fact]
    public Task Actions_AreInTheirOrder_AfterTheSlots() => RunInWindowAsync(1440, (window, _) =>
    {
        var buttons = ActionButtons(window).ToList();
        Assert.Equal([TestCommands.DisabledLabel, "Show or hide details panel"], buttons.Select(Label));

        var view = window.GetVisualDescendants().OfType<PlaceholderView>().Single(view => view.Slot == "view");
        Assert.True(Left(buttons[0], window) > Left(view, window) + view.Bounds.Width);
    });

    [Fact]
    public Task WideWindow_ShowsEveryAction_AndNoMoreButton() => RunInWindowAsync(1440, (window, toolbar) =>
    {
        Assert.Equal(2, ActionButtons(window).Count());
        Assert.All(toolbar.Actions, action => Assert.False(action.IsOverflowed));
        Assert.False(toolbar.Overflow.IsShown);
        Assert.False(MoreButton(window).IsEffectivelyVisible);
    });

    [Fact]
    public Task MinimumWidth_KeepsEveryActionReachable_ThroughTheMoreMenu() => RunInWindowAsync(720, (window, toolbar) =>
    {
        Assert.True(toolbar.Overflow.IsShown);
        var more = MoreButton(window);
        Assert.True(more.IsEffectivelyVisible);
        Assert.Equal("More actions", ToolTip.GetTip(more));
        Assert.Equal("More actions", Avalonia.Automation.AutomationProperties.GetName(more));

        // Each action is either a button on the toolbar or an item in the menu, never neither or both.
        var onToolbar = ActionButtons(window).Select(Label).ToList();
        var inMenu = OpenMenu(more).Select(item => (string?)item.Header).ToList();
        Assert.Equal(
            toolbar.Actions.Select(action => action.Command.PlainLabel).Order(),
            onToolbar.Concat(inMenu.Select(header => header!.Replace("_", string.Empty, StringComparison.Ordinal))).Order());

        // Everything still fits: the More button ends inside the toolbar.
        var toolbarArea = Toolbar(window);
        Assert.True(Left(more, window) + more.Bounds.Width <= Left(toolbarArea, window) + toolbarArea.Bounds.Width);
    });

    [Fact]
    public Task MenuItems_RunTheirCommands_AndShowTheirShortcuts() => RunInWindowAsync(720, (window, _) =>
    {
        var viewModel = (MainWindowViewModel)window.DataContext!;
        var items = OpenMenu(MoreButton(window));
        var toggle = items.Single(item => (string?)item.Header == "_Show or hide details panel");
        var disabled = items.Single(item => (string?)item.Header == "_New library…");
        Assert.Equal(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.D2, Avalonia.Input.KeyModifiers.Control), toggle.InputGesture);
        Assert.False(disabled.IsEffectivelyEnabled);
        Assert.Equal(TestCommands.DisabledReason, ToolTip.GetTip(disabled));

        toggle.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.Layout.Details.IsVisible);
    });

    [Fact]
    public Task Widening_BringsTheActionsBack() => RunInWindowAsync(720, (window, toolbar) =>
    {
        Assert.True(toolbar.Overflow.IsShown);

        window.Width = 1440;
        Dispatcher.UIThread.RunJobs();

        Assert.False(toolbar.Overflow.IsShown);
        Assert.Empty(toolbar.Overflow.Entries);
        Assert.Equal(2, ActionButtons(window).Count());
    });

    [Fact]
    public Task Narrowing_MovesTheLowestPriorityFirst() => RunInWindowAsync(1440, (window, toolbar) =>
    {
        var search = window.GetVisualDescendants().OfType<PlaceholderView>().Single(view => view.Slot == "search");

        // Take away just enough that both no longer fit, but the higher priority one does beside the More button.
        window.Width -= search.Bounds.Width - 160 + 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([TestCommands.DisabledId], toolbar.Actions.Where(action => action.IsOverflowed).Select(action => action.Command.Id));
        Assert.Equal(["Show or hide details panel"], ActionButtons(window).Select(Label));
    });

    private static Button MoreButton(Window window)
        => window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "OverflowButton");

    private static List<MenuItem> OpenMenu(Button more)
    {
        var flyout = (MenuFlyout)more.Flyout!;
        flyout.ShowAt(more);
        Dispatcher.UIThread.RunJobs();
        // The flyout keeps its popup to itself.
        const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        var popup = typeof(Avalonia.Controls.Primitives.PopupFlyoutBase).GetProperties(Flags)
            .Where(property => property.PropertyType == typeof(Avalonia.Controls.Primitives.Popup))
            .Select(property => property.GetValue(flyout))
            .Concat(typeof(Avalonia.Controls.Primitives.PopupFlyoutBase).GetFields(Flags)
                .Where(field => field.FieldType == typeof(Avalonia.Controls.Primitives.Popup))
                .Select(field => field.GetValue(flyout)))
            .OfType<Avalonia.Controls.Primitives.Popup>()
            .First();
        // Left open: the items lose their command bindings when the menu closes. Closing the window closes it.
        return [.. ((Control)popup.Child!).GetVisualDescendants().OfType<MenuItem>()];
    }

    private static IEnumerable<Button> ActionButtons(Window window)
        => window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("ToolbarAction") && button.IsEffectivelyVisible)
            .OrderBy(button => Left(button, window));

    private static string? Label(Button button) => button.GetVisualDescendants().OfType<TextBlock>().First().Text;

    private static double Left(Visual visual, Window window) => visual.TranslatePoint(default, window)!.Value.X;

    private static Visual Toolbar(Window window)
        => window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ToolbarArea");

    private static ViewLocator BuildServices(out ServiceProvider services)
    {
        var collection = new ServiceCollection()
            .AddSingleton<ICommandContributor>(new TestCommands())
            .AddSingleton<IAppPaths>(new FakeAppPaths())
            .AddSingleton<IFolderLauncher>(new FakeFolderLauncher())
            .AddSingleton<ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<Infrastructure.Abstractions.Platform.IPlatformServices>(new FakePlatformServices())
            .AddSingleton<ISettingsStore>(new InMemorySettingsStore())
            .AddSingleton<Infrastructure.Abstractions.Jobs.IJobProgress>(new FakeJobProgress())
            .AddSingleton<Infrastructure.Abstractions.Libraries.ILibrarySession>(new FakeBrowserSession(open: false))
            .AddUseCases()
            .AddUi();

        // After the app's own contributors, so the View slot gets the placeholder.
        collection.AddSingleton<IToolbarContributor>(new PlaceholderSlots());
        collection.AddSingleton<IToolbarContributor>(provider => new TestActions(provider.GetRequiredService<CommandRegistry>()));

        services = collection.BuildServiceProvider();
        return services.GetRequiredService<ViewLocator>().Register<PlaceholderViewModel, PlaceholderView>();
    }

    private static Task RunInWindowAsync(double width, Action<Window, ToolbarViewModel> test) => TestApp.RunAsync(() =>
    {
        var locator = BuildServices(out var services);
        using (services)
        {
            var viewModel = services.GetRequiredService<MainWindowViewModel>();
            var toolbar = services.GetRequiredService<ToolbarViewModel>();
            viewModel.Regions.Toolbar = toolbar;
            var window = new MainWindow { DataContext = viewModel, Width = width, Height = 800 };
            window.DataTemplates.Add(locator);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                test(window, toolbar);
            }
            finally
            {
                window.Close();
            }
        }
    });

    /// <summary>
    /// Fills every slot with a placeholder, as the owning Stories will fill them with real controls.
    /// </summary>
    private sealed class PlaceholderSlots : IToolbarContributor
    {
        public void Register(ToolbarViewModel toolbar)
        {
            toolbar.Search = new PlaceholderViewModel("search");
            toolbar.Filter = new PlaceholderViewModel("filter");
            toolbar.Sort = new PlaceholderViewModel("sort");
            toolbar.View = new PlaceholderViewModel("view");
        }
    }

    /// <summary>
    /// A command that can't run, to show a disabled action.
    /// </summary>
    private sealed class TestCommands : ICommandContributor
    {
        public const string DisabledId = "test.disabled";
        public const string DisabledLabel = "New library…";
        public const string DisabledReason = "Nothing is pending.";

        public void Register(CommandRegistry registry)
            => registry.Add(new AppCommand(DisabledId, CommandGroups.Tools, 900, "Command_NewLibrary", () => { })
            {
                WhyDisabled = () => DisabledReason,
            });
    }

    /// <summary>
    /// Actions a feature adds: an existing command with a shortcut, and the disabled one, out of order.
    /// </summary>
    private sealed class TestActions(CommandRegistry commands) : IToolbarContributor
    {
        public void Register(ToolbarViewModel toolbar)
        {
            toolbar.AddAction(commands[Ui.Shell.ShellCommands.ToggleDetails], order: 200, priority: 200);
            toolbar.AddAction(commands[TestCommands.DisabledId], order: 100, priority: 100);
        }
    }

    private sealed class PlaceholderViewModel(string slot) : ViewModelBase
    {
        public string Slot { get; } = slot;
    }

    private sealed class PlaceholderView : Border
    {
        public const double FixedWidth = 60;

        public PlaceholderView()
        {
            MinWidth = FixedWidth;
            Height = 30;
        }

        public string Slot => ((PlaceholderViewModel)DataContext!).Slot;
    }
}
