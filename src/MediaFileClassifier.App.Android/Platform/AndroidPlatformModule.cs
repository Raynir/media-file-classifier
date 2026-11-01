using Android.Content;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.App.Android.Platform;

/// <summary>
/// Registers the Android implementations of the platform abstractions (E01-S07, E20).
/// </summary>
internal static class AndroidPlatformModule
{
    /// <summary>
    /// Adds the Android platform services. Call it before the shared layers' modules.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="context">The application context.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAndroidPlatform(this IServiceCollection services, Context context)
    {
        services.TryAddSingleton(context);
        services.TryAddSingleton(context.ContentResolver!);
        services.TryAddSingleton<IPlatformServices, AndroidPlatformServices>();
        services.TryAddSingleton<IMediaFileSource, SafMediaFileSource>();
        return services;
    }
}
