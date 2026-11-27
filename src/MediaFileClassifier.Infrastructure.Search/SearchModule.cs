using MediaFileClassifier.Infrastructure.Abstractions.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.Infrastructure.Search;

/// <summary>
/// Registers the search index (E01-S04).
/// </summary>
public static class SearchModule
{
    /// <summary>
    /// Adds <see cref="ISearchIndex"/>. It follows the library session, so it needs <c>AddInfrastructure</c> and
    /// <c>AddDataAccess</c>. The vectors start loading when it's first resolved, so resolve it at startup.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSearch(this IServiceCollection services)
    {
        services.TryAddSingleton<SearchIndex>();
        services.TryAddSingleton<ISearchIndex>(provider => provider.GetRequiredService<SearchIndex>());
        services.TryAddSingleton<IItemVectorSimilarity>(provider => provider.GetRequiredService<SearchIndex>());
        return services;
    }
}
