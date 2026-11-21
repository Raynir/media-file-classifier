using Avalonia;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.SearchResults;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Tests for the list view's header sorting and the grid/list toggle (E14-S02-T03).
/// </summary>
public sealed class ListViewToggleTests
{
    private const int Count = 100_000;

    [Fact]
    public Task Toggle_MidScroll_KeepsTheFirstVisibleItem_BothWays() => TestApp.RunAsync(() =>
    {
        var (grid, _) = ListViewTests.Create(Count);
        grid.IsListView = false;
        var (window, view) = ListViewTests.Show(grid);
        var scroller = ListViewTests.Scroller(view);
        var rowsDown = 400;
        scroller.Offset = new Vector(0, rowsDown * grid.RowHeight);
        ListViewTests.Settle();
        var first = rowsDown * grid.Columns;

        grid.ShowList();
        ListViewTests.Settle();
        var inList = (int)Math.Floor(scroller.Offset.Y / ThumbnailGridViewModel.ListRowHeight);
        Assert.Equal(first, inList);

        grid.ShowGrid();
        ListViewTests.Settle();
        var inGrid = (int)Math.Floor(scroller.Offset.Y / grid.RowHeight) * grid.Columns;
        Assert.Equal(first, inGrid);
        window.Close();
    });

    [Fact]
    public Task Toggle_KeepsTheSelection() => TestApp.RunAsync(async () =>
    {
        var (grid, source) = ListViewTests.Create(100);
        var (window, _) = ListViewTests.Show(grid);
        await grid.Selection.ApplyAsync(7, SelectionGesture.Replace);

        grid.ShowGrid();
        grid.ShowList();

        Assert.True(grid.Selection.IsSelected(source.Ids[7]));
        window.Close();
    });

    [Fact]
    public Task TheView_IsRemembered_AcrossARestart() => TestApp.RunAsync(() =>
    {
        var store = new InMemorySettingsStore();
        var first = Restart(store);
        Assert.False(first.IsListView);

        first.ShowList();
        Assert.True(Restart(store).IsListView);

        first.ShowGrid();
        Assert.False(Restart(store).IsListView);
    });

    [Fact]
    public Task ViewCommands_SwitchTheView() => TestApp.RunAsync(async () =>
    {
        var grid = Restart(new InMemorySettingsStore());
        var registry = new CommandRegistry([new BrowserCommands(() => grid)], Localizer.Current, new FakeCapabilityGate());

        await registry[BrowserCommands.ListView].ExecuteAsync();
        Assert.True(grid.IsListView);
        await registry[BrowserCommands.GridView].ExecuteAsync();
        Assert.False(grid.IsListView);
    });

    [Fact]
    public Task HeaderClick_SortsByTheColumn_AndFlipsOnASecondClick() => TestApp.RunAsync(() =>
    {
        var sort = NewSort();
        var (grid, _) = Create(sort);

        grid.SortByColumn(grid.ListColumns[ListColumnKind.Size]);
        ListViewTests.Settle();
        Assert.Equal(SortMode.Size, sort.SelectedMode!.Mode);
        Assert.True(sort.IsDescending);
        Assert.Equal(sort.DirectionGlyph, grid.ListColumns[ListColumnKind.Size].SortGlyph);
        Assert.Equal(string.Empty, grid.ListColumns[ListColumnKind.Name].SortGlyph);

        grid.SortByColumn(grid.ListColumns[ListColumnKind.Size]);
        ListViewTests.Settle();
        Assert.False(sort.IsDescending);
        Assert.Equal("↑", grid.ListColumns[ListColumnKind.Size].SortGlyph);

        grid.SortByColumn(grid.ListColumns[ListColumnKind.Name]);
        ListViewTests.Settle();
        Assert.Equal(SortMode.Name, sort.SelectedMode.Mode);
        Assert.False(sort.IsDescending);
        Assert.Equal("↑", grid.ListColumns[ListColumnKind.Name].SortGlyph);
        Assert.Equal(string.Empty, grid.ListColumns[ListColumnKind.Size].SortGlyph);
    });

    [Fact]
    public Task HeaderClick_OnAColumnThatCantSort_DoesNothing() => TestApp.RunAsync(() =>
    {
        var sort = NewSort();
        var (grid, _) = Create(sort);
        var before = sort.SelectedMode!.Mode;

        grid.SortByColumn(grid.ListColumns[ListColumnKind.Type]);
        ListViewTests.Settle();

        Assert.Equal(before, sort.SelectedMode!.Mode);
    });

    [Fact]
    public Task SortingFromTheDropdown_MovesTheHeaderGlyph() => TestApp.RunAsync(() =>
    {
        var sort = NewSort();
        var (grid, _) = Create(sort);

        sort.SelectedMode = sort.Modes.First(mode => mode.Mode == SortMode.Name);
        ListViewTests.Settle();

        Assert.Equal("↑", grid.ListColumns[ListColumnKind.Name].SortGlyph);
    });

    private static ThumbnailGridViewModel Restart(ISettingsStore store)
    {
        var source = new FakeResultSource(10);
        var loader = new ThumbnailLoader(new FakeBrowserSession(), new FakeThumbnailCache(), new ThumbnailBitmapCache());
        return new ThumbnailGridViewModel(source, loader, new FakeItemOpener(), new FakeJobSystem(), new FakeTileSizeSender(), new SelectionModel(() => source), settings: store);
    }

    private static (ThumbnailGridViewModel Grid, FakeResultSource Source) Create(SortBarViewModel sort)
    {
        var source = new FakeResultSource(10);
        var loader = new ThumbnailLoader(new FakeBrowserSession(), new FakeThumbnailCache(), new ThumbnailBitmapCache());
        var grid = new ThumbnailGridViewModel(source, loader, new FakeItemOpener(), new FakeJobSystem(), new FakeTileSizeSender(), new SelectionModel(() => source), sort: sort);
        return (grid, source);
    }

    private static SortBarViewModel NewSort()
    {
        var sender = new FakeSender();
        var source = new SearchResultSource(new LibraryItemsSource(sender, new FakeBrowserSession(open: false), TimeProvider.System), sender);
        return new SortBarViewModel(source, TestLocalizer.EnUs());
    }
}
