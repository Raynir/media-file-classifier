using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Capabilities;
using MediaFileClassifier.Infrastructure.NativeDependencies;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.Infrastructure;

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
        services.TryAddSingleton<ISettingsService, JsonSettingsService>();
        services.TryAddSingleton<ICapabilityService, CapabilityService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityContributor, PlatformCapabilityContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityContributor, NativeDependencyCapabilityContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INativeDependencyProbe, FfmpegProbe>(
            _ => new FfmpegProbe(FfmpegProbe.GetDefaultSearchDirectories())));
        return services;
    }
}
