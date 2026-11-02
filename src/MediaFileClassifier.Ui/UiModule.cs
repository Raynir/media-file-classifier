using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Settings;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.Ui.Theming;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.Ui.Views;
using MediaFileClassifier.Ui.Views.Libraries;
using MediaFileClassifier.Ui.Views.Settings;
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
            .Register<QuitConfirmationViewModel, QuitConfirmationView>())
            .Register<WelcomeViewModel, WelcomeView>()
            .Register<CreateLibraryViewModel, CreateLibraryView>());
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
        services.AddSingleton<LibraryDialogs>();
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<LibraryShell>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, LibraryCommands>());

        // Shortcuts (E16-S01): one keymap, bound to physical keys.
        services.AddSingleton<ShortcutReferenceState>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, ShortcutCommands>());
        services.AddSingleton<Keymap>();
        services.AddSingleton<ShortcutHandler>();
        services.AddSingleton<ShortcutReferenceViewModel>();
        services.TryAddSingleton<IKeyboardLayout, QwertyKeyboardLayout>();

        // Status bar (E13-S04). Features add segments through IStatusBarContributor.
        services.AddSingleton<StatusBarViewModel>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
        return services;
    }
}
