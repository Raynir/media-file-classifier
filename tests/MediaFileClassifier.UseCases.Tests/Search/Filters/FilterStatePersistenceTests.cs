using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediaFileClassifier.UseCases.Tests.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Search.Filters;

/// <summary>
/// Tests for keeping the filters across restarts (E10-S02-T03).
/// </summary>
public sealed class FilterStatePersistenceTests
{
    private static readonly FilterRegistry Registry = new(TestFilters.All);

    private readonly InMemorySettingsStore store = new();
    private readonly SearchResultSource source;
    private readonly FilterStateSerializer serializer = new(Registry, NullLogger<FilterStateSerializer>.Instance);

    public FilterStatePersistenceTests()
    {
        var sender = new FakeSender();
        source = new SearchResultSource(new LibraryItemsSource(sender, new FakeLibrarySession(), TimeProvider.System), sender, Registry);
    }

    private static FilterState Videos => FilterState.Empty.Set("type", new MultiSelectValue(["Video"]));

    [Fact]
    public void Filters_AreSavedInTheLibrary_WheneverTheyChange()
    {
        store.IsLibraryOpen = true;
        _ = new FilterStatePersistence(source, store, serializer);

        source.SetFilters(Videos);

        Assert.Equal(Videos, serializer.Deserialize(store.Get(FilterSettings.LastFilters)));
    }

    [Fact]
    public void WhenALibraryOpens_WithRestoreOn_TheSavedFiltersComeBack()
    {
        store.IsLibraryOpen = true;
        store.SetValue(FilterSettings.RestoreOnStart, true);
        store.SetValue(FilterSettings.LastFilters, FilterStateSerializer.Serialize(Videos));
        store.IsLibraryOpen = false;
        _ = new FilterStatePersistence(source, store, serializer);

        store.IsLibraryOpen = true;

        Assert.Equal(Videos, source.Filters);
    }

    [Fact]
    public void WithRestoreOff_TheSavedFiltersStayAway()
    {
        store.IsLibraryOpen = true;
        store.SetValue(FilterSettings.LastFilters, FilterStateSerializer.Serialize(Videos));
        store.IsLibraryOpen = false;
        _ = new FilterStatePersistence(source, store, serializer);

        store.IsLibraryOpen = true;

        Assert.True(source.Filters.IsEmpty);
    }

    [Fact]
    public void WithRestoreOn_ALibraryWithNothingSaved_DropsThePreviousLibrarysFilters()
    {
        store.IsLibraryOpen = true;
        store.SetValue(FilterSettings.RestoreOnStart, true);
        _ = new FilterStatePersistence(source, store, serializer);
        source.SetFilters(Videos);

        // The next library has nothing saved.
        store.SetValue(FilterSettings.LastFilters, string.Empty);
        store.IsLibraryOpen = false;
        store.IsLibraryOpen = true;

        Assert.True(source.Filters.IsEmpty);
    }

    [Fact]
    public void WhenNoLibraryIsOpen_ChangingFilters_SavesNothing()
    {
        _ = new FilterStatePersistence(source, store, serializer);

        source.SetFilters(Videos);

        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void SavedFiltersOfFiltersThatNoLongerExist_AreSkippedOnRestore()
    {
        store.IsLibraryOpen = true;
        store.SetValue(FilterSettings.RestoreOnStart, true);
        store.SetValue(FilterSettings.LastFilters, """{"version":1,"filters":[{"id":"gone","value":{"values":["x"]}}]}""");
        store.IsLibraryOpen = false;
        _ = new FilterStatePersistence(source, store, serializer);

        store.IsLibraryOpen = true;

        Assert.True(source.Filters.IsEmpty);
    }
}
