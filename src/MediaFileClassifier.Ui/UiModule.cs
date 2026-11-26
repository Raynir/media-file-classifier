using MediaFileClassifier.Ui.Analysis;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Editing;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Libraries.Statistics;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Maintenance;
using MediaFileClassifier.Ui.Models;
using MediaFileClassifier.Ui.Navigation;
using MediaFileClassifier.Ui.Notifications;
using MediaFileClassifier.Ui.Rescan;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Search.Filters;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Activity;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.Ui.Settings;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.Ui.Theming;
using MediaFileClassifier.Ui.Toolbar;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.ViewModels.Analysis;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.Ui.ViewModels.Hardware;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.ViewModels.Models;
using MediaFileClassifier.Ui.ViewModels.Navigation;
using MediaFileClassifier.Ui.ViewModels.Activity;
using MediaFileClassifier.Ui.ViewModels.Duplicates;
using MediaFileClassifier.Ui.ViewModels.Processing;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.Ui.Views;
using MediaFileClassifier.Ui.Views.Analysis;
using MediaFileClassifier.Ui.Views.Browser;
using MediaFileClassifier.Ui.Views.Details;
using MediaFileClassifier.Ui.Views.Hardware;
using MediaFileClassifier.Ui.Views.Libraries;
using MediaFileClassifier.Ui.Views.Models;
using MediaFileClassifier.Ui.Views.Navigation;
using MediaFileClassifier.Ui.Views.Activity;
using MediaFileClassifier.Ui.Views.Duplicates;
using MediaFileClassifier.Ui.Views.Processing;
using MediaFileClassifier.Ui.Views.Search;
using MediaFileClassifier.Ui.Views.Settings;
using MediaFileClassifier.Ui.Views.Toolbar;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Common.Notifications;
using MediaFileClassifier.UseCases.Settings;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Playback;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.UseCases.Search.FindSimilar;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Ui;

/// <summary>
/// Registers the shared UI: windows, view models, the view locator, the localizer, the theme, the commands and the settings window.
/// </summary>
public static class UiModule
{
    /// <summary>
    /// Adds the shared UI services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddUi(this IServiceCollection services)
    {
        services.AddSingleton(new ViewLocator()
            .Register<SettingsViewModel, SettingsView>()
            .Register<QuitConfirmationViewModel, QuitConfirmationView>()
            .Register<About.AboutViewModel, AboutView>()
            .Register<RemoveItemsConfirmationViewModel, RemoveItemsConfirmationView>()
            .Register<UnsavedChangesViewModel, UnsavedChangesView>()
            .Register<WelcomeViewModel, WelcomeView>()
            .Register<CreateLibraryViewModel, CreateLibraryView>()
            .Register<ThumbnailGridViewModel, ThumbnailGridView>()
            .Register<IndexedFoldersViewModel, IndexedFoldersView>()
            .Register<ExcludedItemsViewModel, ExcludedItemsView>()
            .Register<LibraryInfoViewModel, LibraryInfoView>()
            .Register<ProcessingPanelViewModel, ProcessingPanelView>()
            .Register<ActivityLogViewModel, ActivityLogView>()
            .Register<DuplicatesViewModel, DuplicatesView>()
            .Register<NavigationPanelViewModel, NavigationPanelView>()
            .Register<DetailsPanelViewModel, DetailsPanelView>()
            .Register<ToolbarViewModel, ToolbarView>()
            .Register<ThumbnailSizeViewModel, ThumbnailSizeView>()
            .Register<SearchBarViewModel, SearchBarView>()
            .Register<ModelManagementViewModel, ModelManagementView>()
            .Register<HardwareSettingsViewModel, HardwareSettingsView>()
            .Register<AnalysisProfileViewModel, AnalysisProfileView>()
            .Register<EndpointsViewModel, EndpointsView>()
            .Register<LibraryInformationSectionViewModel, LibraryInformationSectionView>()
            .Register<LibraryMaintenanceSectionViewModel, LibraryMaintenanceSectionView>()
            .Register<LibraryFoldersSectionViewModel, LibraryEmbeddedSectionView>()
            .Register<LibraryExcludedItemsSectionViewModel, LibraryEmbeddedSectionView>());
        services.AddSingleton(Localizer.Current);
        services.AddSingleton<ThemeService>();
        services.AddSetting(ThemeService.ThemeSetting);
        services.AddSetting(BrowserSettings.ShowFileNames);
        // Notifications (E15-S01): anything can raise a toast through INotificationService.
        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(provider => provider.GetRequiredService<NotificationService>());
        services.AddSingleton<JobNotifications>();
        services.AddSingleton<ToastHostViewModel>();
        services.AddSingleton<ShellLayout>();
        services.AddSingleton<ShellRegions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<LayoutStore>();

        // Commands (E13-S05). Commands follow the capability flags that AddInfrastructure registers.
        services.TryAddSingleton<ICapabilityGate, CapabilityServiceGate>();
        services.TryAddSingleton<IFolderLauncher, AvaloniaFolderLauncher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, ShellCommands>());
        services.AddSingleton<CommandRegistry>();
        services.AddSingleton<MainMenuViewModel>();

        services.AddSingleton<QuitGuard>();
        // Settings window (E17-S01).
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SettingsWindowPresenter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, SettingsCommands>());

        // Libraries (E02).
        services.AddTransient<CreateLibraryViewModel>();
        services.AddSingleton<Func<CreateLibraryViewModel>>(provider => provider.GetRequiredService<CreateLibraryViewModel>);
        services.AddTransient<IndexedFoldersViewModel>();
        services.AddSingleton<Func<IndexedFoldersViewModel>>(provider => provider.GetRequiredService<IndexedFoldersViewModel>);
        services.AddTransient<ExcludedItemsViewModel>();
        services.AddSingleton<Func<ExcludedItemsViewModel>>(provider => provider.GetRequiredService<ExcludedItemsViewModel>);
        services.AddTransient<DeleteLibraryViewModel>();
        services.AddSingleton<Func<DeleteLibraryViewModel>>(provider => provider.GetRequiredService<DeleteLibraryViewModel>);
        services.AddSingleton<LibraryDialogs>();
        services.TryAddSingleton<IModelDialogs, ModelDialogs>();
        services.AddSingleton<ModelBannerViewModel>();

        // Model and hardware settings (E17-S08): sections of AI & Models.
        services.AddSingleton<ModelManagementViewModel>();
        services.AddSingleton(provider => new CustomSettingsSection(
            SettingCategories.AiModels, "Models_Heading", provider.GetRequiredService<ModelManagementViewModel>) { Order = 1000 });
        services.AddSingleton<HardwareSettingsViewModel>();
        services.AddSingleton(provider => new CustomSettingsSection(
            SettingCategories.AiModels, "Hardware_Heading", provider.GetRequiredService<HardwareSettingsViewModel>) { Order = 1100 });

        // Analysis profile and endpoints (E17-S02): more sections of AI & Models.
        services.AddSingleton<AnalysisProfileViewModel>();
        services.AddSingleton(provider => new CustomSettingsSection(
            SettingCategories.AiModels, "Profile_Heading", provider.GetRequiredService<AnalysisProfileViewModel>) { Order = 1200 });
        services.AddSingleton<EndpointsViewModel>();
        services.AddSingleton(provider => new CustomSettingsSection(
            SettingCategories.AiModels, "Endpoints_Heading", provider.GetRequiredService<EndpointsViewModel>) { Order = 1300 });

        // The Library category (E17-S04): sections that host the library views and commands.
        services.AddSingleton<LibraryInformationSectionViewModel>();
        services.AddSingleton(provider => new CustomSettingsSection(
            SettingCategories.Library, "LibrarySection_InformationHeading", provider.GetRequiredService<LibraryInformationSectionViewModel>) { Order = 10 });
        services.AddSingleton<LibraryFoldersSectionViewModel>();
        services.AddSingleton(provider => new CustomSettingsSection(
            SettingCategories.Library, "LibrarySection_FoldersHeading", provider.GetRequiredService<LibraryFoldersSectionViewModel>) { Order = 20 });
        services.AddSingleton<LibraryMaintenanceSectionViewModel>();
        services.AddSingleton(provider => new CustomSettingsSection(
            SettingCategories.Library, "LibrarySection_MaintenanceHeading", provider.GetRequiredService<LibraryMaintenanceSectionViewModel>) { Order = 300 });
        services.AddSingleton<LibraryExcludedItemsSectionViewModel>();
        services.AddSingleton(provider => new CustomSettingsSection(
            SettingCategories.Library, "LibrarySection_ExcludedHeading", provider.GetRequiredService<LibraryExcludedItemsSectionViewModel>) { Order = 400 });

        // Navigation panel (E13-S02). Features add sections through INavigationContributor.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INavigationContributor, LibraryNavigation>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INavigationContributor, FolderNavigation>());
        services.AddSingleton<NavigationPanelViewModel>();
        services.AddSingleton<LibraryOpener>();
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<LibraryShell>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, LibraryCommands>());
        services.TryAddSingleton<FileImporter>();
        services.TryAddSingleton(provider => new DropImportViewModel(
            provider.GetRequiredService<FileImporter>().ImportQuietlyAsync,
            provider.GetRequiredService<LibraryDialogs>().AddDroppedFoldersAsync,
            provider.GetRequiredService<INotificationService>(),
            provider.GetRequiredService<Localizer>(),
            provider.GetService<ILibrarySession>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, ExcludedItemsCommands>());
        services.AddSingleton<RescanRunner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, RescanCommands>());

        // Library maintenance (E02-S06).
        services.AddSingleton<MaintenanceRunner>();
        services.TryAddSingleton<MaintenanceDialogs>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, MaintenanceCommands>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, MaintenanceStatusSegment>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, RetryCommands>());
        services.TryAddSingleton<IAnalysisDialogs, AnalysisDialogs>();
        services.TryAddSingleton<AnalysisRunner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, AnalysisCommands>());

        // Media browser (E14-S01).
        services.AddSingleton<ThumbnailBitmapCache>();
        services.AddSingleton<ThumbnailLoader>();
        services.AddSingleton<UnsavedChangesGuard>();
        services.AddSingleton(provider => new SelectionModel(provider.GetRequiredService<IMediaResultSource>)
        {
            ConfirmChange = provider.GetRequiredService<UnsavedChangesGuard>().ConfirmLeaveAsync,
        });
        services.AddSingleton<ThumbnailGridViewModel>();
        services.AddSingleton<Func<ThumbnailGridViewModel>>(provider => provider.GetRequiredService<ThumbnailGridViewModel>);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, BrowserCommands>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, ItemCommands>());

        // Favorites (E11-S01): one command instance that is also a contributor, and the handler that updates the views.
        services.AddSingleton<ICommandContributor, MetadataEditorCommands>();
        services.TryAddSingleton<FavoriteCommands>();
        services.AddSingleton<ICommandContributor>(provider => provider.GetRequiredService<FavoriteCommands>());

        // Remove from library (E02-S10-T03).
        services.TryAddSingleton<IRemoveItemsDialog, RemoveItemsDialog>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, RemoveItemsCommands>());
        services.AddSetting(RemoveItemsCommands.AskBeforeRemoving);
        services.AddSingleton<Func<DetailsPanelViewModel>>(provider => provider.GetRequiredService<DetailsPanelViewModel>);
        services.AddSingleton<MediatR.INotificationHandler<UseCases.Metadata.ItemsMetadataChanged>, MetadataChangeHandler>();
        services.TryAddSingleton<ITextClipboard, AvaloniaTextClipboard>();
        services.AddSingleton<BrowserContextMenu>();

        // Preview and details panel (E14-S05). Enter and double-click in the grid show the item there.
        services.AddSingleton<PreviewLoader>();
        services.AddSingleton(provider => new VideoPlayerViewModel(
            provider.GetService<IMediaPreviewPlayerFactory>(),
            provider.GetService<ISender>(),
            provider.GetService<ICapabilityGate>(),
            provider.GetRequiredService<ShellLayout>(),
            provider.GetRequiredService<Localizer>(),
            provider.GetService<ILogger<VideoPlayerViewModel>>()));
        services.TryAddSingleton<KeyframePictureLoader>();
        services.AddSingleton(provider => new KeyframeStripViewModel(
            provider.GetService<ISender>(),
            provider.GetService<IKeyframeThumbnails>() is null ? null : provider.GetRequiredService<KeyframePictureLoader>(),
            provider.GetService<ITextClipboard>(),
            provider.GetRequiredService<Localizer>(),
            provider.GetService<ILogger<KeyframeStripViewModel>>()));
        services.AddSingleton<DetailsPanelViewModel>();
        services.AddSingleton<IItemOpener>(provider => provider.GetRequiredService<DetailsPanelViewModel>());

        // Shortcuts (E16-S01): one keymap, bound to physical keys.
        services.AddSingleton<ShortcutReferenceState>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, ShortcutCommands>());
        services.AddSingleton<Keymap>();
        services.AddSingleton<ShortcutHandler>();
        services.AddSingleton<ShortcutReferenceViewModel>();
        services.TryAddSingleton<IKeyboardLayout, QwertyKeyboardLayout>();

        // About (MFC-1023).
        services.AddSingleton<About.AboutState>();
        services.AddSingleton(About.AppVersion.Current);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, About.AboutCommands>());
        services.AddSingleton<About.AboutViewModel>();

        // Status bar (E13-S04). Features add segments through IStatusBarContributor.
        services.AddSingleton<StatusBarViewModel>();

        // Unified search (E09-S01): the box in the toolbar and the notice above the results.
        services.TryAddSingleton<IReferenceImagePicker, AvaloniaReferenceImagePicker>();
        services.AddSingleton(provider => new SearchBarViewModel(
            provider.GetRequiredService<SearchResultSource>(),
            provider.GetRequiredService<ISender>(),
            provider.GetRequiredService<IReferenceImagePicker>(),
            provider.GetRequiredService<Localizer>(),
            thumbnails: provider.GetService<IThumbnailCache>() is null ? null : provider.GetRequiredService<ThumbnailLoader>(),
            analyzeNow: provider.GetService<ILibrarySession>() is null ? null : provider.GetRequiredService<AnalyzeNow>()));
        services.AddSingleton<SearchNoticeViewModel>();
        services.AddSingleton<FilterBarViewModel>();
        services.AddSingleton<SortBarViewModel>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolbarContributor, SearchToolbar>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, FindSimilarCommands>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, SearchCommands>());

        // Toolbar (E13-S03). Features fill its slots and add actions through IToolbarContributor.
        services.AddSingleton<ToolbarViewModel>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolbarContributor, BrowserToolbar>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolbarContributor, LibraryToolbar>());

        // Processing indicator (E05-S02).
        services.AddSingleton<ProgressFeed>();
        services.AddSingleton<ProcessingPause>();
        services.AddSingleton<ProcessingPanelViewModel>();
        services.AddSingleton<ProcessingPanelPresenter>();

        // Duplicates view (E12-S03).
        services.AddSingleton<DuplicatesViewModel>();

        // Activity log (E15-S04).
        services.AddSingleton<IActivityNavigator, ActivityNavigator>();
        services.AddSingleton<ActivityLogViewModel>();
        services.AddSingleton<ActivityLogPresenter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, ActivityCommands>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, ProcessingCommands>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, NavigationCommands>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, ProcessingStatusSegment>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, SelectionStatusSegment>());

        // Library statistics (E02-S05).
        services.AddSingleton<LibraryStatisticsMonitor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, LibrarySummaryStatusSegment>());

        // The pause button (E05-S05) shares order -1 with the summary, so it's registered after it to sit on its left.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, ProcessingPauseSegment>());
        services.AddSingleton<LibraryInfoViewModel>();
        services.AddSingleton<LibraryInfoPresenter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, LibraryInfoCommands>());

        // M1 segments (E13-S04-T02).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, ResultCountStatusSegment>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, OfflineFoldersStatusSegment>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, MonitoringPauseSegment>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, ProviderStatusSegment>());

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
        return services;
    }
}
