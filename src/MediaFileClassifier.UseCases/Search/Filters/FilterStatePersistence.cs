using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Search.SearchResults;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<FilterStatePersistence> logger;
    private bool isReady;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterStatePersistence"/> class.
    /// </summary>
    /// <param name="source">The search results the filters apply to.</param>
    /// <param name="store">The settings.</param>
    /// <param name="serializer">Turns the filters to and from JSON.</param>
    /// <param name="logger">Logs filters that couldn't be saved.</param>
    public FilterStatePersistence(SearchResultSource source, ISettingsStore store, FilterStateSerializer serializer, ILogger<FilterStatePersistence> logger)
    {
        this.source = source;
        this.store = store;
        this.serializer = serializer;
        this.logger = logger;
        store.AvailabilityChanged += (_, _) => Restore();
        source.Changed += (_, _) => Save();
        Restore();
    }

    private void Restore()
    {
        // Nothing is saved until the library that is open has had its turn: until then the filters still belong to the
        // previous library, and writing them would put them in this one.
        isReady = false;
        if (!store.IsAvailable(SettingScope.Library))
        {
            return;
        }

        // A library that opens gets its own filters, or none: the previous library's don't mean anything in it.
        source.SetFilters(store.Get(FilterSettings.RestoreOnStart) ? serializer.Deserialize(store.Get(FilterSettings.LastFilters)) : FilterState.Empty);
        isReady = true;
    }

    private void Save()
    {
        if (!isReady || !store.IsAvailable(SettingScope.Library))
        {
            return;
        }

        try
        {
            // No filters are stored as the default, so a library without any isn't written to.
            var filters = source.Filters;
            store.SetValue(FilterSettings.LastFilters, filters.IsEmpty ? string.Empty : FilterStateSerializer.Serialize(filters));
        }
        catch (InvalidOperationException) when (!store.IsAvailable(SettingScope.Library))
        {
            // The library closed meanwhile; there is nothing to save to.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // The save runs inside whatever changed the results, such as an import adding items: losing the last
            // filters mustn't fail that too.
            logger.LogError(ex, "Couldn't save the filters");
        }
    }
}
