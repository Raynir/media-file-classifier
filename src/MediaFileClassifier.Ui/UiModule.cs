namespace MediaFileClassifier.Ui;

using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.Views;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the shared UI: windows, view models and the view locator.
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
        services.AddSingleton(new ViewLocator());
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
        return services;
    }
}
