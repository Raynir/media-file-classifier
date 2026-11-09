using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Models;
using MediaFileClassifier.Ui.Navigation;
using MediaFileClassifier.Ui.Rescan;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.Ui.Settings;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.Ui.Theming;
using MediaFileClassifier.Ui.Toolbar;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.ViewModels.Models;
using MediaFileClassifier.Ui.ViewModels.Navigation;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.Ui.Views;
using MediaFileClassifier.Ui.Views.Browser;
using MediaFileClassifier.Ui.Views.Details;
using MediaFileClassifier.Ui.Views.Libraries;
using MediaFileClassifier.Ui.Views.Navigation;
using MediaFileClassifier.Ui.Views.Search;
using MediaFileClassifier.Ui.Views.Settings;
using MediaFileClassifier.Ui.Views.Toolbar;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            .Register<WelcomeViewModel, WelcomeView>()
            .Register<CreateLibraryViewModel, CreateLibraryView>()
            .Register<ThumbnailGridViewModel, ThumbnailGridView>()
            .Register<IndexedFoldersViewModel, IndexedFoldersView>()
            .Register<NavigationPanelViewModel, NavigationPanelView>()
            .Register<DetailsPanelViewModel, DetailsPanelView>()
            .Register<ToolbarViewModel, ToolbarView>()
            .Register<ThumbnailSizeViewModel, ThumbnailSizeView>()
            .Register<SearchBarViewModel, SearchBarView>());
        services.AddSingleton(Localizer.Current);
        services.AddSingleton<ThemeService>();
        services.AddSetting(ThemeService.ThemeSetting);
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
        services.AddSingleton<LibraryDialogs>();
        services.TryAddSingleton<IModelDialogs, ModelDialogs>();
        services.AddSingleton<ModelBannerViewModel>();

        // Navigation panel (E13-S02). Features add sections through INavigationContributor.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INavigationContributor, LibraryNavigation>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INavigationContributor, FolderNavigation>());
        services.AddSingleton<NavigationPanelViewModel>();
        services.AddSingleton<LibraryOpener>();
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<LibraryShell>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, LibraryCommands>());
        services.AddSingleton<RescanRunner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, RescanCommands>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, RetryCommands>());

        // Media browser (E14-S01).
        services.AddSingleton<ThumbnailBitmapCache>();
        services.AddSingleton<ThumbnailLoader>();
        services.AddSingleton(provider => new SelectionModel(provider.GetRequiredService<IMediaResultSource>));
        services.AddSingleton<ThumbnailGridViewModel>();
        services.AddSingleton<Func<ThumbnailGridViewModel>>(provider => provider.GetRequiredService<ThumbnailGridViewModel>);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, BrowserCommands>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, ItemCommands>());
        services.TryAddSingleton<ITextClipboard, AvaloniaTextClipboard>();
        services.AddSingleton<BrowserContextMenu>();

        // Preview and details panel (E14-S05). Enter and double-click in the grid show the item there.
        services.AddSingleton<PreviewLoader>();
        services.AddSingleton<DetailsPanelViewModel>();
        services.AddSingleton<IItemOpener>(provider => provider.GetRequiredService<DetailsPanelViewModel>());

        // Shortcuts (E16-S01): one keymap, bound to physical keys.
        services.AddSingleton<ShortcutReferenceState>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, ShortcutCommands>());
        services.AddSingleton<Keymap>();
        services.AddSingleton<ShortcutHandler>();
        services.AddSingleton<ShortcutReferenceViewModel>();
        services.TryAddSingleton<IKeyboardLayout, QwertyKeyboardLayout>();

        // Status bar (E13-S04). Features add segments through IStatusBarContributor.
        services.AddSingleton<StatusBarViewModel>();

        // Unified search (E09-S01): the box in the toolbar and the notice above the results.
        services.AddSingleton<SearchBarViewModel>();
        services.AddSingleton<SearchNoticeViewModel>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolbarContributor, SearchToolbar>());

        // Toolbar (E13-S03). Features fill its slots and add actions through IToolbarContributor.
        services.AddSingleton<ToolbarViewModel>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolbarContributor, BrowserToolbar>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolbarContributor, LibraryToolbar>());

        // Processing indicator (E05-S02).
        services.AddSingleton<ProgressFeed>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, ProcessingStatusSegment>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, SelectionStatusSegment>());

        // M1 segments (E13-S04-T02).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, ResultCountStatusSegment>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, OfflineFoldersStatusSegment>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatusBarContributor, ProviderStatusSegment>());

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
        return services;
    }
}
