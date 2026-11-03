using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.Stages;
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
    /// Adds MediatR with every handler in this assembly (D063), the settings registry (E17-S01), the media browser's result
    /// source (E14-S01) and the import pipeline's stages (E03-S07).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddUseCases(this IServiceCollection services)
    {
        services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(UseCasesModule).Assembly));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SettingRegistry>();
        services.TryAddSingleton<IMediaResultSource, LibraryItemsSource>();
        services.AddSetting(KnownSettings.OpenAiCompatibleEndpoint);
        services.AddSetting(KnownSettings.AnalyzeAutomatically);
        services.AddSetting(KnownSettings.RetainedPreviousRuns);
        services.AddSetting(Libraries.OpenLibrary.LibraryStartupSettings.OpenLastLibrary);

        services.TryAddSingleton<FullHashJobs>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, DiscoverStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, FingerprintStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, MediaStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, ContentHashStage>());
        return services;
    }
}
