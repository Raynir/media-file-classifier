using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Search.Filters;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Search.SearchResults;

namespace MediaFileClassifier.Ui.Tests.Search.Filters;

/// <summary>
/// Tests for the filter bar: the quick chips and the panel that read and write the search's filters (E10-S02).
/// </summary>
public sealed class FilterBarViewModelTests
{
    private readonly Localizer localizer = TestLocalizer.EnUs();

    [Fact]
    public Task Panel_HasAControlForEveryAvailableFilter_InPanelOrder() => TestApp.RunAsync(() =>
    {
        var hidden = TestFilters.Flag("hidden", 50);
        hidden.SetAvailable(false);
        var bar = Create(out _, TestFilters.Tags, TestFilters.Kinds, hidden, TestFilters.Megapixels);

        Assert.Equal(["test-kind", "test-range", "test-tags"], bar.Editors.Select(editor => editor.Id));
        Assert.True(bar.IsVisible);
    });

    [Fact]
    public Task Panel_WithNoFilters_IsHidden() => TestApp.RunAsync(() =>
    {
        var bar = Create(out _);

        Assert.False(bar.IsVisible);
        Assert.Empty(bar.QuickFilters);
    });

    [Fact]
    public Task SettingAControl_AppliesTheFilterToTheSearch() => TestApp.RunAsync(() =>
    {
        var bar = Create(out var source, TestFilters.Favorites, TestFilters.Megapixels);

        Editor<BooleanFilterViewModel>(bar, "favorite").IsOn = true;
        Editor<RangeFilterViewModel>(bar, "test-range").MinText = "3";

        Assert.Equal(new BooleanValue(true), source.Filters.Get("favorite"));
        Assert.Equal(new RangeValue(3, null), source.Filters.Get("test-range"));
    });

    [Fact]
    public Task ClearingAControl_RemovesTheFilterFromTheSearch() => TestApp.RunAsync(() =>
    {
        var bar = Create(out var source, TestFilters.Favorites);
        source.SetFilters(FilterState.Empty.Set("favorite", new BooleanValue(true)));
        Pump.Until(() => Editor<BooleanFilterViewModel>(bar, "favorite").IsOn);

        Editor<BooleanFilterViewModel>(bar, "favorite").Clear();

        Assert.True(source.Filters.IsEmpty);
    });

    [Fact]
    public Task AFilterSetElsewhere_ShowsInItsControl_AndInTheButtonCount() => TestApp.RunAsync(() =>
    {
        var bar = Create(out var source, TestFilters.Favorites, TestFilters.Megapixels);
        Assert.Equal("Filters", bar.ButtonText);

        source.SetFilters(FilterState.Empty.Set("test-range", new RangeValue(1, 2)).Set("favorite", new BooleanValue(true)));
        Pump.Until(() => bar.ActiveCount == 2);

        Assert.True(Editor<BooleanFilterViewModel>(bar, "favorite").IsOn);
        Assert.Equal("Filters (2)", bar.ButtonText);
    });

    [Fact]
    public Task AControlBeingTypedIn_IsNotRewrittenWhenItsOwnChangeComesBack() => TestApp.RunAsync(() =>
    {
        var bar = Create(out _, TestFilters.Megapixels);
        var range = Editor<RangeFilterViewModel>(bar, "test-range");

        range.MinText = "1.";
        Pump.For(TimeSpan.FromMilliseconds(50));

        Assert.Equal("1.", range.MinText);
    });

    [Fact]
    public Task QuickChips_SetTheMediaTypeFilter_AndShowWhichOneIsInForce() => TestApp.RunAsync(() =>
    {
        var bar = Create(out var source, TestFilters.MediaType);
        Assert.Equal(["All", "Images", "Videos"], bar.QuickFilters.Select(chip => chip.Text));
        Assert.True(bar.QuickFilters[0].IsChecked);

        bar.QuickFilters[1].ApplyCommand.Execute(null);
        Assert.Equal(new MultiSelectValue(["Image", "AnimatedImage"]), source.Filters.Get("media-type"));
        Pump.Until(() => bar.QuickFilters[1].IsChecked);
        Assert.False(bar.QuickFilters[0].IsChecked);

        bar.QuickFilters[2].ApplyCommand.Execute(null);
        Assert.Equal(new MultiSelectValue(["Video"]), source.Filters.Get("media-type"));

        bar.QuickFilters[0].ApplyCommand.Execute(null);
        Assert.Null(source.Filters.Get("media-type"));
    });

    [Fact]
    public Task FavoritesChip_AppearsOnlyOnceAFavoriteFilterIsAvailable_AndToggles() => TestApp.RunAsync(() =>
    {
        var favorites = TestFilters.Favorites;
        favorites.SetAvailable(false);
        var bar = Create(out var source, TestFilters.MediaType, favorites);
        Assert.DoesNotContain(bar.QuickFilters, chip => chip.Text == "Favorites");

        favorites.SetAvailable(true);
        Pump.Until(() => bar.QuickFilters.Any(chip => chip.Text == "Favorites"));
        var chip = bar.QuickFilters.Single(item => item.Text == "Favorites");

        chip.ApplyCommand.Execute(null);
        Assert.Equal(new BooleanValue(true), source.Filters.Get("favorite"));

        chip.ApplyCommand.Execute(null);
        Assert.Null(source.Filters.Get("favorite"));
    });

    [Fact]
    public Task AFilterThatBecomesUnavailable_LosesItsControl() => TestApp.RunAsync(() =>
    {
        var flag = TestFilters.Flag("flag");
        var bar = Create(out _, flag);
        Assert.Single(bar.Editors);

        flag.SetAvailable(false);
        Pump.Until(() => bar.Editors.Count == 0);

        Assert.False(bar.IsVisible);
    });

    [Fact]
    public Task Options_OfAMultiSelectFilter_LoadWhenTheBarIsBuilt() => TestApp.RunAsync(() =>
    {
        var bar = Create(out _, TestFilters.Kinds);
        Pump.Until(() => bar.OptionsLoaded.IsCompleted);

        var editor = Assert.IsType<MultiSelectFilterViewModel>(bar.Editors.Single());
        Assert.Equal(3, editor.Options.Count);
    });

    private FilterBarViewModel Create(out SearchResultSource source, params IFilterDefinition[] filters)
    {
        var sender = new FakeSender();
        var registry = new FilterRegistry(filters);
        source = new SearchResultSource(new LibraryItemsSource(sender, new FakeBrowserSession(open: false), TimeProvider.System), sender, registry);
        return new FilterBarViewModel(source, registry, localizer);
    }

    private static T Editor<T>(FilterBarViewModel bar, string id)
        where T : FilterEditorViewModel => Assert.IsType<T>(bar.Editors.Single(editor => editor.Id == id));
}
