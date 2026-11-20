using MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;
using MediaFileClassifier.Infrastructure.Abstractions.Playback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.Infrastructure.Playback;

/// <summary>
/// Registers in-app video playback (E14-S06, D070).
/// </summary>
public static class PlaybackModule
{
    /// <summary>
    /// Adds the LibVLC runtime, its probe (the <c>VideoPlayback</c> capability) and the preview player factory. The
    /// head must ship the native LibVLC for its platform.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPlayback(this IServiceCollection services)
    {
        services.TryAddSingleton<LibVlcRuntime>();
        services.TryAddSingleton<IMediaPreviewPlayerFactory, LibVlcPreviewPlayerFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INativeDependencyProbe, LibVlcProbe>());
        return services;
    }
}
