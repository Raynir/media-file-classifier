using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediaFileClassifier.UseCases.Tests.Libraries;

namespace MediaFileClassifier.UseCases.Tests.Search;

/// <summary>
/// Tests for <see cref="SortChoice"/> and for keeping the search and browse orders across restarts (E10-S04-T02).
/// </summary>
public sealed class SortStatePersistenceTests
{
    private readonly InMemorySettingsStore store = new();
    private readonly SearchResultSource source;

    public SortStatePersistenceTests()
    {
        var sender = new FakeSender();
        source = new SearchResultSource(new LibraryItemsSource(sender, new FakeLibrarySession(), TimeProvider.System), sender);
    }

    [Theory]
    [InlineData(SortMode.Relevance, true, false)]
    [InlineData(SortMode.Relevance, false, false)]
    [InlineData(SortMode.Name, false, false)]
    [InlineData(SortMode.Name, true, true)]
    [InlineData(SortMode.Date, true, false)]
    [InlineData(SortMode.Date, false, true)]
    [InlineData(SortMode.Size, false, true)]
    [InlineData(SortMode.ImportDate, true, false)]
    public void ReverseSort_ReversesTheModesNaturalDirection(SortMode mode, bool descending, bool reverse) =>
        Assert.Equal(reverse, new SortChoice(mode, descending).ReverseSort);

    [Theory]
    [InlineData(SortMode.Name, false)]
    [InlineData(SortMode.Date, true)]
    [InlineData(SortMode.Size, true)]
    [InlineData(SortMode.ImportDate, true)]
    public void Natural_StartsNamesAToZ_AndTheOthersNewestOrLargest(SortMode mode, bool descending) =>
        Assert.Equal(new SortChoice(mode, descending), SortChoice.Natural(mode));

    [Theory]
    [InlineData("")]
    [InlineData("Name")]
    [InlineData("Name up")]
    [InlineData("Shape asc")]
    [InlineData("99 asc")]
    [InlineData("name asc")]
    [InlineData(null)]
    public void TryParse_RejectsWhatIsNotAnOrder(string? text) => Assert.False(SortChoice.TryParse(text, out _));

    [Fact]
    public void TryParse_ReadsWhatToStringWrites()
    {
        foreach (var mode in Enum.GetValues<SortMode>())
        {
            foreach (var descending in new[] { false, true })
            {
                var choice = new SortChoice(mode, descending);
                Assert.True(SortChoice.TryParse(choice.ToString(), out var read));
                Assert.Equal(choice, read);
            }
        }
    }

    [Fact]
    public void Orders_AreSaved_WheneverTheyChange()
    {
        _ = new SortStatePersistence(source, store);

        source.SetSort(new SortChoice(SortMode.Size, Descending: false));
        source.SetText("car");
        source.SetSort(new SortChoice(SortMode.Name, Descending: true));

        Assert.Equal("Size asc", store.Get(SortSettings.BrowseSort));
        Assert.Equal("Name desc", store.Get(SortSettings.SearchSort));
    }

    [Fact]
    public void SavedOrders_ComeBack()
    {
        store.SetValue(SortSettings.BrowseSort, "Name asc");
        store.SetValue(SortSettings.SearchSort, "Date desc");

        _ = new SortStatePersistence(source, store);

        Assert.Equal(new SortChoice(SortMode.Name, false), source.BrowseSort);
        Assert.Equal(new SortChoice(SortMode.Date, true), source.SearchSort);
        Assert.Equal(new SortChoice(SortMode.Name, false), source.Sort);
    }

    [Theory]
    [InlineData("garbage", "Relevance asc")]
    [InlineData("Relevance desc", "")]
    public void UnreadableOrders_GiveTheDefaults(string browse, string search)
    {
        store.SetValue(SortSettings.BrowseSort, browse);
        store.SetValue(SortSettings.SearchSort, search);

        _ = new SortStatePersistence(source, store);

        Assert.Equal(SortChoice.BrowseDefault, source.BrowseSort);
        Assert.Equal(browse == "garbage" ? new SortChoice(SortMode.Relevance, false) : SortChoice.SearchDefault, source.SearchSort);
    }
}
