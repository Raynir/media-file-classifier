using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MediaFileClassifier.Prototypes.Playback;

internal sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // [folder …] [--engine 0-3] [--file name]
            var args = desktop.Args ?? [];
            var folders = new List<string>();
            var engine = 0;
            string? file = null;
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--engine" when index + 1 < args.Length:
                        engine = int.Parse(args[++index], CultureInfo.InvariantCulture);
                        break;
                    case "--file" when index + 1 < args.Length:
                        file = args[++index];
                        break;
                    default:
                        folders.Add(args[index]);
                        break;
                }
            }

            var window = new MainWindow(MediaFolders.Resolve(folders));
            window.Select(engine, file);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
