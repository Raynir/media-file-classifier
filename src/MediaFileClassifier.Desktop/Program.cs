namespace MediaFileClassifier.Desktop;

using Avalonia;
using MediaFileClassifier.Ui;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.Hosting;

/// <summary>
/// The desktop entry point and composition root.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services
            .AddUseCases()
            .AddUi();

        using var host = builder.Build();
        host.Start();
        try
        {
            return BuildAvaloniaApp(host.Services).StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Avalonia configuration used by the XAML designer.
    /// </summary>
    /// <returns>The configured app builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services)
        => AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont();
}
