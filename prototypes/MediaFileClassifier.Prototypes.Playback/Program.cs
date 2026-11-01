using Avalonia;
using MediaFileClassifier.Prototypes.Playback.Measurement;

namespace MediaFileClassifier.Prototypes.Playback;

/// <summary>
/// Playback spike prototype (MFC-54). With no arguments, or with folders, it opens the preview window.
/// <c>measure [folder]</c> runs the measurements in the console and prints a Markdown report.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "measure")
        {
            return MeasurementRunner.Run(args[1..]);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
