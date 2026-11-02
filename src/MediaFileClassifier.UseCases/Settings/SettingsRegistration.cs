using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Settings;

/// <summary>
/// Lets features register their settings (E17-S01-T01).
/// </summary>
public static class SettingsRegistration
{
    /// <summary>
    /// Registers a setting, so it appears in the settings window. Read it with <see cref="ISettingsStore.Get{T}"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="setting">The setting, usually a static field of the feature.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSetting(this IServiceCollection services, SettingDefinition setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        services.AddSingleton(setting);
        return services;
    }
}
