using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.Views.Browser;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Tests for the list view's column setup (E14-S02-T02): show, hide, resize and move columns, and keep the setup
/// across a restart.
/// </summary>
public sealed class ListColumnsTests
{
    [Fact]
    public Task Defaults_ShowEveryColumnButTheFolder_ThumbnailFirst() => TestApp.RunAsync(() =>
    {
        var columns = new ListColumnsViewModel();

        Assert.Equal(ListColumnKind.Thumbnail, columns.Columns[0].Kind);
        Assert.Equal(Enum.GetValues<ListColumnKind>().Length, columns.Columns.Count);
        Assert.Equal([ListColumnKind.Folder], columns.Columns.Where(column => !column.IsVisible).Select(column => column.Kind));
    });

    [Fact]
    public Task Setup_SurvivesARestart() => TestApp.RunAsync(() =>
    {
        var store = new InMemorySettingsStore();
        var columns = new ListColumnsViewModel(store);
        columns.Resize(ListColumnKind.Name, 400);
        columns.SetVisible(ListColumnKind.Folder, true);
        columns.SetVisible(ListColumnKind.Duration, false);
        columns.Move(ListColumnKind.Size, 1);
        columns.SaveNow();

        var restarted = new ListColumnsViewModel(store);

        Assert.Equal(columns.Columns.Select(Describe), restarted.Columns.Select(Describe));
        Assert.Equal(ListColumnKind.Size, restarted.Columns[1].Kind);
        Assert.Equal(400, restarted[ListColumnKind.Name].Width);
        Assert.True(restarted[ListColumnKind.Folder].IsVisible);
        Assert.False(restarted[ListColumnKind.Duration].IsVisible);
    });

    [Fact]
    public Task Changes_AreSavedAfterAPause_NotOnEveryStep() => TestApp.RunAsync(async () =>
    {
        var store = new InMemorySettingsStore();
        var columns = new ListColumnsViewModel(store);
        var writes = store.WriteCount;

        for (var width = 300; width < 340; width += 4)
        {
            columns.Resize(ListColumnKind.Name, width);
        }

        Assert.Equal(writes, store.WriteCount);
        await Task.Delay(700);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(writes + 1, store.WriteCount);
        Assert.Contains("Name:336", store.Get(BrowserSettings.ListColumns), StringComparison.Ordinal);
    });

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("Name:abc,Type:,:5,Thumbnail:10,Name:Infinity")]
    public void Parse_BadText_GivesTheDefaults(string text)
    {
        Assert.Equal(new ListColumnsViewModel().Columns.Select(Describe), ListColumnsViewModel.Parse(text).Select(Describe));
    }

    [Fact]
    public void Parse_KeepsOrderAndWidthsInRange_AppendsMissingColumns_AndNeverHidesTheName()
    {
        var columns = ListColumnsViewModel.Parse("Size:5,!Name:99999,Bogus:10,!Type:90,Size:200");

        Assert.Equal([ListColumnKind.Thumbnail, ListColumnKind.Size, ListColumnKind.Name, ListColumnKind.Type], columns.Take(4).Select(column => column.Kind));
        Assert.Equal(Enum.GetValues<ListColumnKind>().Length, columns.Count);
        Assert.Equal(ListColumnViewModel.MinWidth, columns[1].Width);
        Assert.Equal(ListColumnViewModel.MaxWidth, columns[2].Width);
        Assert.True(columns[2].IsVisible);
        Assert.False(columns[3].IsVisible);
    }

    [Fact]
    public Task Thumbnail_CantBeMovedResizedOrHidden_AndNothingMovesBeforeIt() => TestApp.RunAsync(() =>
    {
        var columns = new ListColumnsViewModel();
        var width = columns[ListColumnKind.Thumbnail].Width;

        columns.Resize(ListColumnKind.Thumbnail, 300);
        columns.SetVisible(ListColumnKind.Thumbnail, false);
        columns.SetVisible(ListColumnKind.Name, false);
        columns.Move(ListColumnKind.Thumbnail, 5);
        columns.Move(ListColumnKind.Favorite, 0);

        Assert.Equal(width, columns[ListColumnKind.Thumbnail].Width);
        Assert.True(columns[ListColumnKind.Thumbnail].IsVisible);
        Assert.True(columns[ListColumnKind.Name].IsVisible);
        Assert.Equal([ListColumnKind.Thumbnail, ListColumnKind.Favorite], columns.Columns.Take(2).Select(column => column.Kind));
    });

    [Fact]
    public Task Reset_PutsTheDefaultsBack() => TestApp.RunAsync(() =>
    {
        var columns = new ListColumnsViewModel();
        columns.Resize(ListColumnKind.Name, 500);
        columns.Move(ListColumnKind.Status, 1);
        columns.SetVisible(ListColumnKind.Type, false);

        columns.ResetCommand.Execute(null);

        Assert.Equal(new ListColumnsViewModel().Columns.Select(Describe), columns.Columns.Select(Describe));
    });

    [Fact]
    public Task HeaderEdgeDrag_ResizesTheColumn_AndTheRowsFollow() => TestApp.RunAsync(() =>
    {
        var (grid, _) = ListViewTests.Create(20);
        var (window, view) = ListViewTests.Show(grid);
        var thumb = view.GetVisualDescendants().OfType<Thumb>().First(thumb => thumb.DataContext is ListColumnViewModel { Kind: ListColumnKind.Name });

        thumb.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent, Vector = new Vector(60, 0), Source = thumb });
        ListViewTests.Settle();

        Assert.Equal(340, grid.ListColumns[ListColumnKind.Name].Width);
        var row = view.GetVisualDescendants().OfType<ListCellsPanel>().First(panel => panel.DataContext is ThumbnailTileViewModel);
        var name = row.Children.First(child => ListCellsPanel.GetColumn(child) == ListColumnKind.Name);
        Assert.Equal(340, name.Bounds.Width);
        window.Close();
    });

    [Fact]
    public Task HeaderMenu_ShowsAndHidesColumns_AndResets() => TestApp.RunAsync(() =>
    {
        var (grid, _) = ListViewTests.Create(20);
        var (window, view) = ListViewTests.Show(grid);

        var menu = ListHeaderView.BuildMenu(grid.ListColumns);
        var folder = menu.OfType<MenuItem>().Single(item => (string?)item.Header == grid.ListColumns[ListColumnKind.Folder].Name);
        Assert.False(folder.IsChecked);
        Assert.DoesNotContain(menu.OfType<MenuItem>(), item => (string?)item.Header == grid.ListColumns[ListColumnKind.Name].Name);
        folder.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        ListViewTests.Settle();

        Assert.True(grid.ListColumns[ListColumnKind.Folder].IsVisible);
        var row = view.GetVisualDescendants().OfType<ListCellsPanel>().First(panel => panel.DataContext is ThumbnailTileViewModel);
        Assert.True(row.Children.First(child => ListCellsPanel.GetColumn(child) == ListColumnKind.Folder).IsVisible);

        menu.OfType<MenuItem>().Last().Command!.Execute(null);
        ListViewTests.Settle();
        Assert.False(grid.ListColumns[ListColumnKind.Folder].IsVisible);
        Assert.Equal(grid.ListColumns.Columns.Count, view.GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("ListHeaderCell")));
        window.Close();
    });

    [Theory]
    [InlineData(10, ListColumnKind.Thumbnail)]
    [InlineData(48, ListColumnKind.Name)]
    [InlineData(330, ListColumnKind.Type)]
    [InlineData(99999, ListColumnKind.Favorite)]
    public Task ColumnAt_FindsTheColumnUnderThePointer(double x, ListColumnKind expected) => TestApp.RunAsync(() =>
    {
        Assert.Equal(expected, ListHeaderView.ColumnAt(new ListColumnsViewModel(), x)?.Kind);
    });

    [Theory]
    [InlineData(ListColumnKind.Favorite, true, "")]
    [InlineData(ListColumnKind.Thumbnail, false, "")]
    [InlineData(ListColumnKind.Name, false, "Name")]
    public Task HeaderIcon_IsOnlyForTheFavoriteColumn(ListColumnKind kind, bool hasIcon, string header) => TestApp.RunAsync(() =>
    {
        var column = new ListColumnViewModel(kind, 40, true);

        Assert.Equal(hasIcon, column.HasHeaderIcon);
        Assert.Equal(header, column.Header);
    });

    private static string Describe(ListColumnViewModel column) => $"{column.Kind}:{column.Width}:{column.IsVisible}";
}
