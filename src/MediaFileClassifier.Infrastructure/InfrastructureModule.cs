namespace MediaFileClassifier.Infrastructure;

using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Paths;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Registers the general infrastructure implementations.
/// </summary>
public static class InfrastructureModule
{
    /// <summary>
    /// Adds the general infrastructure services. Services a platform head registered first are kept.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<IAppPaths, AppPaths>();
        return services;
    }
}
