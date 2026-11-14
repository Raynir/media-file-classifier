using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Search.Filters;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Views.Search;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Search.SearchResults;

namespace MediaFileClassifier.Ui.Tests.Search.Filters;

/// <summary>
/// Tests for the filter panel's views (E10-S02-T01).
/// </summary>
public sealed class FilterBarViewTests
{
    [Fact]
    public Task Panel_ShowsAControlForEveryKind_AndTheHeaderRowShowsTheQuickChips() => TestApp.RunAsync(() =>
    {
        var sender = new FakeSender();
        var registry = new FilterRegistry([TestFilters.MediaType, TestFilters.Kinds, TestFilters.Dates, TestFilters.Megapixels, TestFilters.Favorites, TestFilters.Tags]);
        var source = new SearchResultSource(new LibraryItemsSource(sender, new FakeBrowserSession(open: false), TimeProvider.System), sender, registry);
        var bar = new FilterBarViewModel(source, registry, TestLocalizer.EnUs());
        Pump.Until(() => bar.OptionsLoaded.IsCompleted);

        var panel = new FilterPanelView { DataContext = bar };
        var barView = new FilterBarView { DataContext = bar };
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { barView, panel } } };
        window.Show();
        Dispatcher();

        Assert.NotEmpty(panel.GetVisualDescendants().OfType<CheckBox>());
        Assert.NotEmpty(panel.GetVisualDescendants().OfType<ComboBox>());
        Assert.NotEmpty(panel.GetVisualDescendants().OfType<CalendarDatePicker>());
        Assert.True(panel.GetVisualDescendants().OfType<TextBox>().Count() >= 3);
        Assert.Equal(5, barView.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("FilterChip")));
        window.Close();
    });

    [Fact]
    public Task QuickChips_AreToggleButtons_ThatShowTheOneInForce() => TestApp.RunAsync(() =>
    {
        var sender = new FakeSender();
        var registry = new FilterRegistry([TestFilters.MediaType]);
        var source = new SearchResultSource(new LibraryItemsSource(sender, new FakeBrowserSession(open: false), TimeProvider.System), sender, registry);
        var bar = new FilterBarViewModel(source, registry, TestLocalizer.EnUs());
        var barView = new FilterBarView { DataContext = bar };
        var window = new Window { Width = 800, Height = 600, Content = barView };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var chips = barView.GetVisualDescendants().OfType<ToggleButton>().ToList();
        Assert.Equal(["All", "Images", "Videos"], chips.Select(chip => chip.Content));
        Assert.True(chips[0].IsChecked);

        bar.QuickFilters[2].ApplyCommand.Execute(null);
        Pump.Until(() => chips[2].IsChecked == true);
        Assert.False(chips[0].IsChecked);
        window.Close();
    });

    private static void Dispatcher() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
}
