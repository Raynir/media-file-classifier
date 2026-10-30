using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
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
        return services;
    }
}
