namespace MediaFileClassifier.Desktop;

using Avalonia;
using MediaFileClassifier.Ui;

/// <summary>
/// The desktop entry point.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Avalonia configuration; also used by the visual designer.
    /// </summary>
    /// <returns>The configured app builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
