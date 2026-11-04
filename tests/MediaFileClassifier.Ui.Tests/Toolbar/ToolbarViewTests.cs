using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Browser;
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
/// Tests for the toolbar in the main window (E13-S03-T01): each slot shows the control a feature puts in it, in
/// a fixed order at every window width.
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

        Assert.True(search.Bounds.Width > 400, $"search is {search.Bounds.Width} wide");
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

    private static double Left(Visual visual, Window window) => visual.TranslatePoint(default, window)!.Value.X;

    private static Visual Toolbar(Window window)
        => window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ToolbarArea");

    private static ViewLocator BuildServices(out ServiceProvider services)
    {
        var collection = new ServiceCollection()
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
