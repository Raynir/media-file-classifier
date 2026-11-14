using Avalonia.Controls;
using Avalonia.VisualTree;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Views.Search;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.SearchResults;

namespace MediaFileClassifier.Ui.Tests.Search;

/// <summary>
/// Tests for the sort control above the results (E10-S04-T02).
/// </summary>
public sealed class SortBarViewModelTests
{
    [Fact]
    public Task Browsing_OffersEveryModeButRelevance_AndStartsByDateTakenNewestFirst() => TestApp.RunAsync(() =>
    {
        var bar = Create(out _);

        Assert.Equal([SortMode.Name, SortMode.Date, SortMode.Size, SortMode.ImportDate], bar.Modes.Select(mode => mode.Mode));
        Assert.Equal(SortMode.Date, bar.SelectedMode!.Mode);
        Assert.Equal("Date taken", bar.SelectedMode.Text);
        Assert.True(bar.IsDescending);
        Assert.True(bar.IsDirectionVisible);
        Assert.Equal("Descending", bar.DirectionText);
    });

    [Fact]
    public Task Searching_OffersRelevance_WithNoDirection_AndKeepsTheBrowseOrderApart() => TestApp.RunAsync(() =>
    {
        var bar = Create(out var source);
        bar.SelectedMode = bar.Modes.Single(mode => mode.Mode == SortMode.Name);
        Assert.Equal(new SortChoice(SortMode.Name, Descending: false), source.BrowseSort);
        Pump.Until(() => !bar.IsDescending);

        source.SetText("car");
        Pump.Until(() => bar.Modes.Count == 5);

        Assert.Equal(SortMode.Relevance, bar.SelectedMode!.Mode);
        Assert.False(bar.IsDirectionVisible);

        source.SetText(string.Empty);
        Pump.Until(() => bar.Modes.Count == 4);

        Assert.Equal(SortMode.Name, bar.SelectedMode!.Mode);
    });

    [Fact]
    public Task DirectionButton_ReversesTheOrder() => TestApp.RunAsync(() =>
    {
        var bar = Create(out var source);

        bar.ToggleDirectionCommand.Execute(null);
        Pump.Until(() => !bar.IsDescending);

        Assert.Equal(new SortChoice(SortMode.Date, Descending: false), source.Sort);
        Assert.Equal("Ascending", bar.DirectionText);
    });

    [Fact]
    public Task PickingAMode_SortsInItsUsualDirection_AndIsRemembered() => TestApp.RunAsync(() =>
    {
        var store = new InMemorySettingsStore();
        var source = NewSource();
        var bar = new SortBarViewModel(source, TestLocalizer.EnUs(), new SortStatePersistence(source, store));

        bar.SelectedMode = bar.Modes.Single(mode => mode.Mode == SortMode.Size);

        Assert.Equal(new SortChoice(SortMode.Size, Descending: true), source.Sort);
        Assert.Equal("Size desc", store.Get(SortSettings.BrowseSort));
    });

    [Fact]
    public Task View_ShowsTheDropdown_AndTheDirectionButtonWithAnAccessibleName() => TestApp.RunAsync(() =>
    {
        var bar = Create(out _);
        var view = new SortBarView { DataContext = bar };
        var window = new Window { Width = 400, Height = 200, Content = view };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var combo = view.GetVisualDescendants().OfType<ComboBox>().Single();
        var button = view.GetVisualDescendants().OfType<Button>().Single(candidate => candidate.Name == "SortDirection");
        Assert.Equal(4, combo.ItemCount);
        Assert.Equal("Sort by", Avalonia.Automation.AutomationProperties.GetName(combo));
        Assert.Equal("Descending", Avalonia.Automation.AutomationProperties.GetName(button));
        Assert.True(button.IsVisible);
        window.Close();
    });

    private static SortBarViewModel Create(out SearchResultSource source)
    {
        source = NewSource();
        return new SortBarViewModel(source, TestLocalizer.EnUs());
    }

    private static SearchResultSource NewSource()
    {
        var sender = new FakeSender();
        return new SearchResultSource(new LibraryItemsSource(sender, new FakeBrowserSession(open: false), TimeProvider.System), sender);
    }
}
