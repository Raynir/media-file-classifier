using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Matching;
using MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Capabilities;
using MediaFileClassifier.Infrastructure.Discovery;
using MediaFileClassifier.Infrastructure.Hardware;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Infrastructure.Matching;
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
        services.TryAddSingleton<ILibrarySettingValues, NoLibrarySettingValues>();
        services.TryAddSingleton<ISettingsStore, SettingsStore>();
        services.TryAddSingleton<IHardwareDetector, PortableHardwareDetector>();
        services.TryAddSingleton<IHardwareInfo, HardwareInfo>();
        services.TryAddSingleton<ICapabilityService, CapabilityService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityContributor, PlatformCapabilityContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityContributor, NativeDependencyCapabilityContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INativeDependencyProbe, FfmpegProbe>(
            _ => new FfmpegProbe(FfmpegProbe.GetDefaultSearchDirectories())));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IFolderDiscovery, FolderDiscovery>();
        services.TryAddSingleton<IFileHasher, XxHashFileHasher>();
        services.TryAddSingleton<IPerceptualHasher, SkiaPerceptualHasher>();
        services.TryAddSingleton<IMediaHasher, MediaHasher>();
        services.TryAddSingleton<IFileMatcherFactory, FileMatcherFactory>();
        return services;
    }
}
