using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Ui.Platform;
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
        services.TryAddSingleton<IMediaFileSource, FileSystemMediaFileSource>();
        services.TryAddSingleton<IHardwareDetector, WindowsHardwareDetector>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INativeDependencyProbe, SkiaSharpProbe>());
        services.TryAddSingleton<IKeyboardLayout, WindowsKeyboardLayout>();
        return services;
    }
}
