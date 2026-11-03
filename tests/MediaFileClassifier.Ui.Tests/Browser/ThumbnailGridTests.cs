using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
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
    public Task Open_OpensTheFocusedTilesItem() => TestApp.RunAsync(() =>
    {
        var opener = new FakeItemOpener();
        var (grid, _, source, _) = Create(Count, opener: opener);
        var (window, view) = Show(grid);

        var border = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ThumbnailTile"));
        border.Focus();
        grid.OpenAsync(null).GetAwaiter().GetResult();

        Assert.Equal([source.Ids[0]], opener.Opened);
        window.Close();
    });

    private static (ThumbnailGridViewModel Grid, FakeTileSizeSender Sender, FakeResultSource Source, FakeJobSystem Jobs) Create(
        int count, int storedSize = LibrarySettings.DefaultThumbnailTileSize, FakeItemOpener? opener = null)
    {
        var source = new FakeResultSource(count);
        var sender = new FakeTileSizeSender { StoredSize = storedSize };
        var jobs = new FakeJobSystem();
        var loader = new ThumbnailLoader(new FakeBrowserSession(), new FakeThumbnailCache(), new ThumbnailBitmapCache());
        return (new ThumbnailGridViewModel(source, loader, opener ?? new FakeItemOpener(), jobs, sender), sender, source, jobs);
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

    private static List<ThumbnailTileViewModel> Tiles(ThumbnailGridView view) =>
        [.. view.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("ThumbnailTile") && border.IsEffectivelyVisible)
            .Select(border => border.DataContext)
            .OfType<ThumbnailTileViewModel>()];
}
