using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MediaFileClassifier.Ui.Theming;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui;

/// <summary>
/// The Avalonia application shared by all platform heads.
/// </summary>
public partial class App : Application
{
    private readonly IServiceProvider? services;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class without services. Used by the XAML designer only.
    /// </summary>
    public App()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    /// <param name="services">Services built by the platform head's composition root.</param>
    public App(IServiceProvider services)
    {
        this.services = services;
    }

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (services is not null)
        {
            DataTemplates.Add(services.GetRequiredService<ViewLocator>());
            services.GetRequiredService<ThemeService>().Apply();
        }
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (services is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
        }
        else if (services is not null && ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Android (E20) shows the shell without a window.
            singleView.MainView = new MainView { DataContext = services.GetRequiredService<MainWindowViewModel>() };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
