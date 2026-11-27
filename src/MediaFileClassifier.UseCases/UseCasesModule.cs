using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.Stages;
using MediaFileClassifier.UseCases.Libraries.RemoveFolder;
using MediaFileClassifier.UseCases.Rescan;
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
    /// source (E14-S01), the import pipeline's stages (E03-S07) and the folder removal stage (E02-S03).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddUseCases(this IServiceCollection services)
    {
        services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(UseCasesModule).Assembly));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SettingRegistry>();
        services.TryAddSingleton<Search.Filters.FilterRegistry>();
        services.TryAddSingleton<Infrastructure.Abstractions.AI.IAnalysisProvenanceSource, Analysis.Outdated.AnalysisProvenanceSource>();
        services.TryAddSingleton<Search.Filters.FilterStateSerializer>();
        services.TryAddSingleton<Search.Filters.FilterStatePersistence>();
        services.TryAddSingleton<Search.SearchResults.SortStatePersistence>();
        services.AddSetting(Search.Filters.FilterSettings.RestoreOnStart);
        services.TryAddSingleton<Libraries.CheckFolderAvailability.FolderStatusChanges>();
        services.TryAddSingleton<Libraries.CheckFolderAvailability.FolderAvailabilityMonitor>();
        services.TryAddSingleton<Monitoring.MonitoringPause>();
        services.TryAddSingleton<Monitoring.MonitoringTally>();
        services.AddSetting(Monitoring.MonitoringSettings.NotifyThreshold);
        services.TryAddSingleton<Monitoring.FolderWatchManager>();
        services.TryAddSingleton<Monitoring.ChangeProcessing.HeldDeletes>();
        services.TryAddSingleton<Monitoring.ChangeProcessing.ChangeProcessor>();
        services.AddSetting(Monitoring.MonitoringSettings.IgnorePatterns);
        services.AddSetting(Monitoring.MonitoringSettings.PollIntervalMinutes);
        services.TryAddSingleton<Monitoring.FolderLocations>();
        services.TryAddSingleton<Monitoring.FolderPoller>();
        services.TryAddSingleton<Monitoring.QuickSyncStarter>();
        services.TryAddSingleton<Monitoring.ReconnectCatchUp>();
        services.AddSetting(KnownSettings.QuickSyncOnOpen);
        services.AddSetting(KnownSettings.MonitoringEnabled);
        services.TryAddSingleton<LibraryItemsSource>();
        services.TryAddSingleton<Metadata.UndoBulkEdit.BulkUndoStore>();
        services.TryAddSingleton<Activity.ActivityChanges>();
        services.TryAddSingleton<Activity.ActivityJobRecorder>();
        services.TryAddSingleton<Duplicates.Visual.VisualDuplicateDetector>();
        services.TryAddSingleton<Duplicates.DuplicateGroupMaintainer>();
        services.TryAddSingleton<Embeddings.ItemEmbeddingAnalyzer>();
        services.TryAddSingleton<Embeddings.SemanticQueryEncoder>();
        services.TryAddSingleton<Embeddings.EmbeddingModelMigration>();
        services.TryAddSingleton<Search.SearchResults.SearchResultSource>();
        services.TryAddSingleton<IMediaResultSource>(provider => provider.GetRequiredService<Search.SearchResults.SearchResultSource>());
        services.TryAddSingleton<Search.FindSimilar.AnalyzeNow>();
        services.AddSetting(KnownSettings.InformationalNotifications);
        services.AddSetting(KnownSettings.OpenAiCompatibleEndpoint);
        services.AddSetting(KnownSettings.AnalyzeAutomatically);
        services.TryAddSingleton<Analysis.AutoAnalyze.AutoAnalyzeBatcher>();
        services.AddSetting(KnownSettings.RetainedPreviousRuns);
        services.AddSetting(Libraries.OpenLibrary.LibraryStartupSettings.OpenLastLibrary);

        services.TryAddSingleton<FullHashJobs>();
        services.TryAddSingleton<FolderScanGate>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, DiscoverStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, FingerprintStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, MediaStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, ContentHashStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, RemoveFolderStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, Exclusions.ExclusionCleanupStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, Duplicates.DetectDuplicates.DuplicateReconcileStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, Duplicates.DetectDuplicates.VisualReconcileStage>());
        services.TryAddSingleton<MoveDetector>();
        services.TryAddSingleton<Reconciler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, ReconcileStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, RescanCheckStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, RefreshMediaStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, Monitoring.ChangeProcessing.ApplyChangesStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, Maintenance.RegenerateThumbnails.RegenerateThumbnailStage>());
        services.TryAddSingleton<Tags.TagNormalizer>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStageHandler, Analysis.Pipeline.AnalysisStage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionClassifier, ImportExceptionClassifier>());
        return services;
    }
}
