using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Registers the Windows implementations of the platform abstractions (E01-S07).
/// </summary>
internal static class DesktopPlatformModule
{
    /// <summary>
    /// Adds the Windows platform services. Call it before the shared layers' modules.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddDesktopPlatform(this IServiceCollection services)
    {
        services.TryAddSingleton<IPlatformServices, WindowsPlatformServices>();
        return services;
    }
}
