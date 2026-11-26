using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.DataAccess.Libraries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.Infrastructure.DataAccess;

/// <summary>
/// Registers the SQLite persistence layer (E01-S03).
/// </summary>
public static class DataAccessModule
{
    /// <summary>
    /// Adds the library store. Each open library brings its own repositories and maintenance services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddDataAccess(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILibraryStore, LibraryStore>();
        services.TryAddSingleton<ILibraryDeleter, LibraryDeleter>();

        // Library settings live in the open library (E02-S02); this replaces the "no library" storage of AddInfrastructure.
        services.Replace(ServiceDescriptor.Singleton<ILibrarySettingValues, OpenLibrarySettingValues>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionClassifier, Database.DatabaseExceptionClassifier>());

        // Filters (E10-S01): the file format filter needs the format registry of AddMedia(), the folder and tag filters the library
        // session of AddInfrastructure().
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.MediaTypeFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.FavoriteFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.FileFormatFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.DateFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.ResolutionFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.StatusFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.FolderFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.TagsFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.OutdatedAnalysisFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.NewSuggestionsFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.HasDuplicatesFilter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFilterDefinition, Search.Filters.HideOfflineFilter>());
        return services;
    }
}
