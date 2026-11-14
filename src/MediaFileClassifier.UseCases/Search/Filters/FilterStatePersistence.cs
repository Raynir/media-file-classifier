using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Search.SearchResults;

namespace MediaFileClassifier.UseCases.Search.Filters;

/// <summary>
/// Keeps the filters in force across restarts (E10-S02-T03): writes them to the open library's settings whenever they
/// change, and puts them back when a library opens if <see cref="FilterSettings.RestoreOnStart"/> is on.
/// </summary>
public sealed class FilterStatePersistence
{
    private readonly SearchResultSource source;
    private readonly ISettingsStore store;
    private readonly FilterStateSerializer serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterStatePersistence"/> class.
    /// </summary>
    /// <param name="source">The search results the filters apply to.</param>
    /// <param name="store">The settings.</param>
    /// <param name="serializer">Turns the filters to and from JSON.</param>
    public FilterStatePersistence(SearchResultSource source, ISettingsStore store, FilterStateSerializer serializer)
    {
        this.source = source;
        this.store = store;
        this.serializer = serializer;
        store.AvailabilityChanged += (_, _) => Restore();
        source.Changed += (_, _) => Save();
        Restore();
    }

    private void Restore()
    {
        if (!store.IsAvailable(SettingScope.Library) || !store.Get(FilterSettings.RestoreOnStart))
        {
            return;
        }

        // A library that opens gets its own filters, or none: the previous library's don't mean anything in it.
        source.SetFilters(serializer.Deserialize(store.Get(FilterSettings.LastFilters)));
    }

    private void Save()
    {
        if (!store.IsAvailable(SettingScope.Library))
        {
            return;
        }

        try
        {
            store.SetValue(FilterSettings.LastFilters, FilterStateSerializer.Serialize(source.Filters));
        }
        catch (InvalidOperationException)
        {
            // The library closed meanwhile; there is nothing to save to.
        }
    }
}
