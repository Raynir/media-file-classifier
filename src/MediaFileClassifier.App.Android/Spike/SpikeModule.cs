using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.App.Android.Spike;

/// <summary>
/// Registers the smoke-test page and its checks (MFC-18).
/// </summary>
internal static class SpikeModule
{
    /// <summary>
    /// Adds the smoke-test services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSpike(this IServiceCollection services)
    {
        services.AddSingleton<SqliteCheck>();
        services.AddSingleton<OnnxCheck>();
        services.AddSingleton<SafCheck>();
        services.AddSingleton<VectorSearchCheck>();
        services.AddSingleton<SpikeViewModel>();
        return services;
    }
}
