using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.Views.Browser;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Tests for the thumbnail grid (E14-S01): virtualisation at 100k results, layout, keeping the scroll position,
/// the visible-first boost, the size control and opening items.
/// </summary>
public sealed class ThumbnailGridTests
{
    private const int Count = 100_000;

    [Fact]
    public Task Grid_With100kResults_RealisesOnlyTheTilesOnScreen() => TestApp.RunAsync(() =>
    {
        var (grid, _, source, _) = Create(Count);
        var (window, view) = Show(grid);

        var tiles = Tiles(view);
        Assert.InRange(tiles.Count, 1, 200);
        Assert.Equal((Count + grid.Columns - 1) / grid.Columns, grid.Rows.Count);
        Assert.InRange(grid.Pages.LoadedPageCount, 1, 3);

        var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        Settle();

        Assert.InRange(Tiles(view).Count, 1, 200);
        Assert.Contains(Tiles(view), tile => tile.Index == Count - 1 && tile.Item?.Id == source.Ids[Count - 1]);
        Assert.InRange(grid.Pages.LoadedPageCount, 1, ResultPageCache.MaxPages);
        window.Close();
    });

    [Theory]
    [InlineData(1000, 160, 5)]
    [InlineData(1000, 96, 9)]
    [InlineData(1000, 384, 2)]
    [InlineData(100, 384, 1)]
    public Task Columns_FillTheWidth(double width, int tileSize, int columns) => TestApp.RunAsync(() =>
    {
        var (grid, _, _, _) = Create(50);
        grid.ActivateAsync().GetAwaiter().GetResult();
        grid.TileSize = tileSize;

        grid.UpdateViewport(width, 0, 800, 1);

        Assert.Equal(columns, grid.Columns);
        Assert.Equal((50 + columns - 1) / columns, grid.Rows.Count);
    });

    [Theory]
    [InlineData(100, 96)]
    [InlineData(170, 160)]
    [InlineData(177, 192)]
    [InlineData(1000, 384)]
    public void Snap_KeepsTheSizeOnTheSteps(double size, int expected) => Assert.Equal(expected, ThumbnailGridViewModel.Snap(size));

    [Fact]
    public Task SizeKeys_StepWithinTheRange_AndTheSizeIsSavedForTheLibrary() => TestApp.RunAsync(() =>
    {
        var (grid, sender, _, _) = Create(10, storedSize: 352);
        grid.ActivateAsync().GetAwaiter().GetResult();
        Assert.Equal(352, grid.TileSize);
        Assert.Empty(sender.Saved);

        grid.Larger();
        grid.Larger();
        Assert.Equal(ThumbnailGridViewModel.MaxTileSize, grid.TileSize);
        grid.RunPendingWorkAsync().GetAwaiter().GetResult();
        Assert.Equal([384], sender.Saved);

        for (var step = 0; step < 20; step++)
        {
            grid.Smaller();
        }

        Assert.Equal(ThumbnailGridViewModel.MinTileSize, grid.TileSize);
    });

    [Fact]
    public Task TileSize_PicksTheThumbnailResolution() => TestApp.RunAsync(() =>
    {
        var (grid, _, _, _) = Create(10);
        grid.UpdateViewport(1000, 0, 800, 2);
        Assert.Equal(ThumbnailSize.Large, grid.ThumbnailSize);

        grid.TileSize = 128;

        Assert.Equal(ThumbnailSize.Small, grid.ThumbnailSize);
    });

    [Fact]
    public Task NewItems_KeepTheFirstVisibleItemInPlace() => TestApp.RunAsync(() =>
    {
        var (grid, _, source, _) = Create(10_000);
        grid.ActivateAsync().GetAwaiter().GetResult();
        grid.UpdateViewport(1000, 0, 800, 1);
        var row = 100;
        grid.UpdateViewport(1000, row * grid.RowHeight, 800, 1);
        grid.Pages.Request(row * grid.Columns, row * grid.Columns);
        var anchor = grid.Pages.Get(row * grid.Columns)!.Id;
        double? scrolledTo = null;
        grid.ScrollRequested += (_, offset) => scrolledTo = offset;

        source.InsertAtTop(grid.Columns * 3);
        Dispatcher.UIThread.RunJobs();
        grid.RunPendingWorkAsync().GetAwaiter().GetResult();

        Assert.Equal(10_000 + (grid.Columns * 3), grid.Count);
        Assert.Equal((row + 3) * grid.RowHeight, scrolledTo);
        Assert.Equal(anchor, source.Ids[(row + 3) * grid.Columns]);
    });

    [Fact]
    public Task VisibleTilesWithoutThumbnails_AreBoosted() => TestApp.RunAsync(() =>
    {
        var (grid, _, source, jobs) = Create(Count);
        source.Override = index => FakeResultSource.Summary(source.Ids[index], index) with { ThumbnailFailed = index == 1 };
        var (window, view) = Show(grid);

        Pump.Until(() => Tiles(view).All(tile => tile.Item is not null));
        grid.RunPendingWorkAsync().GetAwaiter().GetResult();

        var boosted = Assert.Single(jobs.Boosts);
        Assert.Contains(source.Ids[0], boosted);
        Assert.DoesNotContain(source.Ids[1], boosted);
        Assert.InRange(boosted.Count, grid.Columns, 200);
        Assert.DoesNotContain(source.Ids[Count - 1], boosted);

        grid.Deactivate();
        Assert.Empty(jobs.Boosts[^1]);
        window.Close();
    });

    [Fact]
    public Task Open_OpensTheFocusedItem() => TestApp.RunAsync(() =>
    {
        var opener = new FakeItemOpener();
        var (grid, _, source, _) = Create(Count, opener: opener);
        var (window, _) = Show(grid);

        grid.ClickAsync(3, SelectionGesture.Replace).GetAwaiter().GetResult();
        grid.OpenAsync(null).GetAwaiter().GetResult();

        Assert.Equal([source.Ids[3]], opener.Opened);
        window.Close();
    });

    [Fact]
    public Task PreviousAndNext_FollowTheResultOrder_AndTheGridTakesTheFocusBack() => TestApp.RunAsync(() =>
    {
        var (grid, _, _, _) = Create(Count);
        var (window, view) = Show(grid);
        var registry = new CommandRegistry([new BrowserCommands(() => grid)], Localizer.Current, new FakeCapabilityGate());
        void Run(string id) => registry[id].ExecuteAsync().GetAwaiter().GetResult();

        // A row ends at the last column; next goes on to the first item of the next row.
        grid.ClickAsync(grid.Columns - 1, SelectionGesture.Replace).GetAwaiter().GetResult();
        Run(BrowserCommands.NextItem);
        Assert.Equal((grid.Columns, 1), (grid.Selection.FocusIndex, grid.Selection.Count));
        Run(BrowserCommands.PreviousItem);
        Run(BrowserCommands.PreviousItem);
        Assert.Equal((grid.Columns - 2, 1), (grid.Selection.FocusIndex, grid.Selection.Count));

        grid.ClickAsync(0, SelectionGesture.Replace).GetAwaiter().GetResult();
        Run(BrowserCommands.PreviousItem);
        Assert.Equal(0, grid.Selection.FocusIndex);

        grid.RequestFocus();
        Assert.True(Scroller(view).IsFocused);
        window.Close();
    });

    [Fact]
    public Task Keys_MoveTheFocusAndSelect_LikeExplorer() => TestApp.RunAsync(() =>
    {
        var (grid, _, source, _) = Create(Count);
        var (window, view) = Show(grid);
        var columns = grid.Columns;
        Scroller(view).Focus();

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Equal((0, 1), (grid.Selection.FocusIndex, grid.Selection.Count));
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Assert.Equal((1 + columns, columns + 1), (grid.Selection.FocusIndex, grid.Selection.Count));
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
        Assert.Equal(columns + 2, grid.Selection.Count);
        Settle();
        Assert.Contains(Tiles(view), tile => tile.Index == 2 + columns && tile.IsFocused && tile.IsSelected);
        Assert.Contains(Tiles(view), tile => tile.Index == 1 && tile.IsSelected && !tile.IsFocused);

        window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        Settle();
        Assert.Equal((Count - 1, 1), (grid.Selection.FocusIndex, grid.Selection.Count));
        Assert.Contains(Tiles(view), tile => tile.Index == Count - 1 && tile.IsSelected && tile.Item?.Id == source.Ids[Count - 1]);

        // Ctrl+A and Esc are keymap commands (KeymapTests); here the commands themselves.
        grid.SelectAllAsync().GetAwaiter().GetResult();
        Assert.Equal(Count, grid.Selection.Count);
        grid.ClearSelection();
        Assert.Equal(0, grid.Selection.Count);
        window.Close();
    });

    [Fact]
    public Task Clicks_Select_CtrlToggles_ShiftExtends_AndEmptySpaceClears() => TestApp.RunAsync(() =>
    {
        var (grid, _, _, _) = Create(Count);
        var (window, view) = Show(grid);
        var tiles = view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ThumbnailTile") && b.IsEffectivelyVisible).ToList();
        Point Center(int index) => tiles.Single(b => ((ThumbnailTileViewModel)b.DataContext!).Index == index)
            .TranslatePoint(new Point(20, 20), window)!.Value;

        window.MouseDown(Center(1), MouseButton.Left);
        window.MouseUp(Center(1), MouseButton.Left);
        window.MouseDown(Center(4), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(Center(4), MouseButton.Left, RawInputModifiers.Shift);
        Assert.Equal(4, grid.Selection.Count);
        window.MouseDown(Center(2), MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(Center(2), MouseButton.Left, RawInputModifiers.Control);
        Assert.Equal(3, grid.Selection.Count);
        Settle();
        Assert.Contains(Tiles(view), tile => tile.Index == 3 && tile.IsSelected);
        Assert.Contains(Tiles(view), tile => tile.Index == 2 && !tile.IsSelected);
        Assert.Contains("Selected", tiles.Single(b => ((ThumbnailTileViewModel)b.DataContext!).Index == 3).Classes);

        // The gap between the first two tiles.
        var gap = Center(0) + new Point(grid.TileSize - 20 + (ThumbnailGridViewModel.Gap / 2), 0);
        window.MouseDown(gap, MouseButton.Left);
        window.MouseUp(gap, MouseButton.Left);
        Assert.Equal(0, grid.Selection.Count);
        window.Close();
    });

    [Fact]
    public Task RightClick_SelectsAnUnselectedTile_KeepsASelectedOne_AndClearsOnTheGap() => TestApp.RunAsync(() =>
    {
        var (grid, _, _, _) = Create(Count);
        var (window, view) = Show(grid);
        var tiles = view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ThumbnailTile") && b.IsEffectivelyVisible).ToList();
        Point Center(int index) => tiles.Single(b => ((ThumbnailTileViewModel)b.DataContext!).Index == index)
            .TranslatePoint(new Point(20, 20), window)!.Value;
        void RightClick(Point point)
        {
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
        }

        grid.ClickAsync(1, SelectionGesture.Replace).GetAwaiter().GetResult();
        grid.ClickAsync(3, SelectionGesture.Range).GetAwaiter().GetResult();

        RightClick(Center(2));
        Assert.Equal((3, 2, 1), (grid.Selection.Count, grid.Selection.FocusIndex, grid.Selection.AnchorIndex));

        RightClick(Center(5));
        Assert.Equal((1, 5), (grid.Selection.Count, grid.Selection.FocusIndex));

        RightClick(Center(0) + new Point(grid.TileSize - 20 + (ThumbnailGridViewModel.Gap / 2), 0));
        Assert.Equal(0, grid.Selection.Count);
        window.Close();
    });

    [Fact]
    public Task Selection_SurvivesARefresh_AndClearsWhenTheLibraryCloses() => TestApp.RunAsync(() =>
    {
        var (grid, _, source, _) = Create(50);
        var (window, _) = Show(grid);
        grid.ClickAsync(5, SelectionGesture.Replace).GetAwaiter().GetResult();
        var selected = source.Ids[5];

        source.InsertAtTop(3);
        Settle();
        grid.RunPendingWorkAsync().GetAwaiter().GetResult();

        Assert.True(grid.Selection.IsSelected(selected));
        Assert.Equal((1, 8), (grid.Selection.Count, grid.Selection.FocusIndex));

        grid.Deactivate();
        Assert.Equal((0, -1), (grid.Selection.Count, grid.Selection.FocusIndex));
        window.Close();
    });

    private static (ThumbnailGridViewModel Grid, FakeTileSizeSender Sender, FakeResultSource Source, FakeJobSystem Jobs) Create(
        int count, int storedSize = LibrarySettings.DefaultThumbnailTileSize, FakeItemOpener? opener = null)
    {
        var source = new FakeResultSource(count);
        var sender = new FakeTileSizeSender { StoredSize = storedSize };
        var jobs = new FakeJobSystem();
        var loader = new ThumbnailLoader(new FakeBrowserSession(), new FakeThumbnailCache(), new ThumbnailBitmapCache());
        return (new ThumbnailGridViewModel(source, loader, opener ?? new FakeItemOpener(), jobs, sender, new SelectionModel(() => source)), sender, source, jobs);
    }

    private static (Window Window, ThumbnailGridView View) Show(ThumbnailGridViewModel grid)
    {
        grid.ActivateAsync().GetAwaiter().GetResult();
        var view = new ThumbnailGridView { DataContext = grid };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Settle();
        return (window, view);
    }

    private static void Settle()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static ScrollViewer Scroller(ThumbnailGridView view) => view.GetVisualDescendants().OfType<ScrollViewer>().First();

    private static List<ThumbnailTileViewModel> Tiles(ThumbnailGridView view) =>
        [.. view.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("ThumbnailTile") && border.IsEffectivelyVisible)
            .Select(border => border.DataContext)
            .OfType<ThumbnailTileViewModel>()];
}
