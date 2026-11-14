using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.UseCases.Search.SearchResults;

/// <summary>
/// Keeps the search and browse orders across restarts (E10-S04-T02): puts them back when it is created and writes them
/// whenever the user changes one. A stored value that can't be read gives the default.
/// </summary>
public sealed class SortStatePersistence
{
    private readonly SearchResultSource source;
    private readonly ISettingsStore store;

    /// <summary>
    /// Initializes a new instance of the <see cref="SortStatePersistence"/> class.
    /// </summary>
    /// <param name="source">The results the orders apply to.</param>
    /// <param name="store">The settings.</param>
    public SortStatePersistence(SearchResultSource source, ISettingsStore store)
    {
        this.source = source;
        this.store = store;
        source.SetSorts(
            Read(SortSettings.SearchSort, SortChoice.SearchDefault),
            Read(SortSettings.BrowseSort, SortChoice.BrowseDefault));
        source.Changed += (_, _) => Save();
    }

    private SortChoice Read(TextSetting setting, SortChoice fallback) =>
        SortChoice.TryParse(store.Get(setting), out var choice) ? choice : fallback;

    private void Save()
    {
        try
        {
            // The store ignores a value that didn't change, so the many other changes of the source write nothing.
            store.SetValue(SortSettings.SearchSort, source.SearchSort.ToString());
            store.SetValue(SortSettings.BrowseSort, source.BrowseSort.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The store logged it; the order still applies until the app closes.
        }
    }
}
