using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.UseCases;

/// <summary>
/// Registers the use case layer.
/// </summary>
public static class UseCasesModule
{
    /// <summary>
    /// Adds MediatR with every handler in this assembly (D063), and the settings registry (E17-S01).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddUseCases(this IServiceCollection services)
    {
        services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(UseCasesModule).Assembly));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SettingRegistry>();
        services.AddSetting(KnownSettings.OpenAiCompatibleEndpoint);
        services.AddSetting(KnownSettings.AnalyzeAutomatically);
        services.AddSetting(KnownSettings.RetainedPreviousRuns);
        return services;
    }
}
