using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.Views.Browser;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Tests for the list view (E14-S02): virtualisation and smooth scrolling at 100k results, and the cells.
/// </summary>
public sealed class ListViewTests
{
    private const int Count = 100_000;

    [Fact]
    public Task List_With100kResults_RealisesOnlyTheRowsOnScreen() => TestApp.RunAsync(() =>
    {
        var (grid, source) = Create(Count);
        var (window, view) = Show(grid);

        Assert.Equal(1, grid.Columns);
        Assert.Equal(Count, grid.Rows.Count);
        Assert.InRange(Rows(view).Count, 1, 60);

        var scroller = Scroller(view);
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        Settle();

        Assert.InRange(Rows(view).Count, 1, 60);
        Assert.Contains(Rows(view), row => row.Index == Count - 1 && row.Item?.Id == source.Ids[Count - 1]);
        Assert.InRange(grid.Pages.LoadedPageCount, 1, ResultPageCache.MaxPages);
        window.Close();
    });

    /// <summary>
    /// The Done-when of E14-S02-T01: scrolling through 100k rows is smooth. Each step scrolls like a mouse wheel
    /// notch (3 rows), with jumps across the whole list as a scroll bar drag does. A step's layout must fit in a frame
    /// (16.7 ms) on average; the 95th percentile is reported, not checked, as it swings with the machine's load. Headless, so it
    /// times layout and bindings, not drawing. Measured on 2026-11-21: mean 4.6 ms, p95 13 ms, max 31–39 ms (the
    /// thumbnail grid in the same harness: mean 6.6 ms, p95 23 ms).
    /// </summary>
    [Fact]
    public Task Scrolling_Through100kRows_FitsInAFrame() => TestApp.RunAsync(() =>
    {
        var (grid, _) = Create(Count);
        var (window, view) = Show(grid);
        var scroller = Scroller(view);
        var times = new List<double>();
        var step = 3 * ThumbnailGridViewModel.ListRowHeight;

        // Warm up the templates and the page cache.
        for (var i = 0; i < 20; i++)
        {
            Scroll(scroller, scroller.Offset.Y + step);
        }

        var watch = new Stopwatch();
        for (var i = 0; i < 600; i++)
        {
            var target = i % 100 == 99
                ? scroller.Extent.Height * (i / 600.0)
                : scroller.Offset.Y + step;
            watch.Restart();
            Scroll(scroller, target);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        var mean = times.Average();
        var p95 = times[(int)(times.Count * 0.95)];
        var max = times[^1];
        var report = $"List scroll step at 100k rows: mean {mean:F2} ms, p95 {p95:F2} ms, max {max:F2} ms";
        TestContext.Current.TestOutputHelper?.WriteLine(report);
        if (Environment.GetEnvironmentVariable("MFC_MEASURE_OUT") is { Length: > 0 } output)
        {
            File.AppendAllText(output, report + Environment.NewLine);
        }

        Assert.True(mean < 16.7, $"mean {mean:F2} ms");
        window.Close();
    });

    [Fact]
    public Task Row_ShowsTheItemsColumns() => TestApp.RunAsync(() =>
    {
        var (grid, source) = Create(10);
        source.Override = index => new MediaItemSummary(
            source.Ids[index], "clip.mp4", "mp4", MediaType.Video, 2_202_009, DateTimeOffset.UnixEpoch, MediaItemStatus.Pending, null,
            TimeSpan.FromSeconds(42), false, IsFavorite: true, DateTaken: new DateTime(2025, 6, 1, 14, 30, 0), Width: 1920, Height: 1080,
            Folder: @"D:\Media\Trips");
        var (window, view) = Show(grid);

        var row = Rows(view).First(tile => tile.Index == 0);
        Assert.Equal("clip.mp4", row.FileName);
        Assert.Equal("MP4", row.TypeText);
        Assert.Equal(MediaFileClassifier.Ui.Localization.Format.FileSize(2_202_009), row.SizeText);
        Assert.Equal("1920 × 1080", row.ResolutionText);
        Assert.Equal("0:42", row.LengthText);
        Assert.Equal(new DateTime(2025, 6, 1, 14, 30, 0).ToString("g", System.Globalization.CultureInfo.CurrentCulture), row.DateTakenText);
        Assert.Equal(@"D:\Media\Trips", row.Folder);
        Assert.False(string.IsNullOrEmpty(row.StatusText));
        Assert.Single(row.Badges);
        window.Close();
    });

    [Fact]
    public Task HiddenColumns_HaveNoVisibleCells_AndTheRestFollowTheOrder() => TestApp.RunAsync(() =>
    {
        var (grid, _) = Create(10);
        var (window, view) = Show(grid);

        var cells = view.GetVisualDescendants().OfType<ListCellsPanel>().First(panel => panel.DataContext is ThumbnailTileViewModel { Index: 0 });
        var folder = cells.Children.First(child => ListCellsPanel.GetColumn(child) == ListColumnKind.Folder);
        Assert.False(folder.IsVisible);

        var visible = cells.Children.Where(child => child.IsVisible).OrderBy(child => child.Bounds.X).Select(ListCellsPanel.GetColumn).ToList();
        Assert.Equal(grid.ListColumns.Columns.Where(column => column.IsVisible).Select(column => column.Kind), visible);
        Assert.Equal(grid.ListColumns.TotalWidth, cells.Bounds.Width);
        window.Close();
    });

    [Fact]
    public Task Toggle_SwapsTheTemplates() => TestApp.RunAsync(() =>
    {
        var (grid, _) = Create(100);
        var (window, view) = Show(grid);
        Assert.NotEmpty(Rows(view));

        grid.ShowGridCommand.Execute(null);
        Settle();

        Assert.True(grid.IsGridView);
        Assert.Empty(Rows(view));
        Assert.Contains(view.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("ThumbnailTile") && border.IsEffectivelyVisible);
        Assert.False(view.GetVisualDescendants().OfType<ListHeaderView>().Single().IsEffectivelyVisible);
        window.Close();
    });

    internal static (ThumbnailGridViewModel Grid, FakeResultSource Source) Create(int count, ISettingsStore? settings = null)
    {
        var source = new FakeResultSource(count);
        var loader = new ThumbnailLoader(new FakeBrowserSession(), new FakeThumbnailCache(), new ThumbnailBitmapCache());
        var grid = new ThumbnailGridViewModel(source, loader, new FakeItemOpener(), new FakeJobSystem(), new FakeTileSizeSender(), new SelectionModel(() => source), settings: settings)
        {
            IsListView = true,
        };
        return (grid, source);
    }

    internal static (Window Window, ThumbnailGridView View) Show(ThumbnailGridViewModel grid)
    {
        grid.ActivateAsync().GetAwaiter().GetResult();
        var view = new ThumbnailGridView { DataContext = grid };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Settle();
        return (window, view);
    }

    internal static void Settle()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    internal static ScrollViewer Scroller(ThumbnailGridView view) => view.GetVisualDescendants().OfType<ScrollViewer>().First(scroller => scroller.Name == "Scroller");

    internal static List<ThumbnailTileViewModel> Rows(ThumbnailGridView view) =>
        [.. view.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("ListRow") && border.IsEffectivelyVisible)
            .Select(border => border.DataContext)
            .OfType<ThumbnailTileViewModel>()];

    private static void Scroll(ScrollViewer scroller, double offset)
    {
        scroller.Offset = new Vector(0, offset);
        Dispatcher.UIThread.RunJobs();
        TopLevel.GetTopLevel(scroller)!.UpdateLayout();
    }
}
