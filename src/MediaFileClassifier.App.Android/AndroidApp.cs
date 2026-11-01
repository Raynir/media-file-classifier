using Android.Content;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MediaFileClassifier.App.Android.Platform;
using MediaFileClassifier.App.Android.Spike;
using MediaFileClassifier.Infrastructure;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.DataAccess;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Ui;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UiApp = MediaFileClassifier.Ui.App;

namespace MediaFileClassifier.App.Android;

/// <summary>
/// The Android application and composition root (E20). It builds the same service graph as the desktop head,
/// with the Android platform services.
/// </summary>
[Application]
public sealed class AndroidApp : AvaloniaAndroidApplication<UiApp>
{
    private ServiceProvider? services;

    /// <summary>
    /// Initializes a new instance of the <see cref="AndroidApp"/> class. Called by the Android runtime.
    /// </summary>
    /// <param name="javaReference">The Java object.</param>
    /// <param name="transfer">Ownership of the reference.</param>
    public AndroidApp(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    /// <summary>
    /// Gets the running application.
    /// </summary>
    public static AndroidApp Current { get; private set; } = null!;

    /// <summary>
    /// Gets the app's services.
    /// </summary>
    public IServiceProvider Services => services ?? throw new InvalidOperationException("The app hasn't started yet.");

    /// <inheritdoc />
    public override void OnCreate()
    {
        Current = this;
        StartupClock.Mark("Application.OnCreate");
        services = BuildServices(this);
        services.GetRequiredService<ICapabilityService>().RefreshAsync().GetAwaiter().GetResult();
        StartupClock.Mark("Services built");
        base.OnCreate();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            services?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Runs smoke-test checks once the shell is up, e.g. from <c>adb shell am start --es spike "sqlite"</c> (MFC-18).
    /// </summary>
    /// <param name="script">Checks separated by <c>;</c>.</param>
    public void RunSpikeScript(string script)
        => Dispatcher.UIThread.Post(() => _ = Services.GetRequiredService<SpikeViewModel>().RunScriptAsync(script));

    /// <inheritdoc />
    protected override AppBuilder CreateAppBuilder()
        => AppBuilder.Configure(() => new UiApp(Services)).UseAndroid();

    /// <inheritdoc />
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .AfterSetup(_ => Dispatcher.UIThread.Post(ShowSpikePage));

    private static ServiceProvider BuildServices(Context context)
    {
        var appPaths = new AppPaths(context.FilesDir!.AbsolutePath);
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
            .AddSingleton<IAppPaths>(appPaths)
            .AddAndroidPlatform(context)
            .AddInfrastructure()
            .AddDataAccess()
            .AddUseCases()
            .AddUi()
            .AddSpike();
        return services.BuildServiceProvider();
    }

    // The smoke test shows in the shell's content area, through the shared view locator, with the side panels
    // closed to fit a phone.
    private void ShowSpikePage()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime { MainView.DataContext: MainWindowViewModel shell })
        {
            Services.GetRequiredService<ViewLocator>().Register<SpikeViewModel, SpikeView>();
            var layout = Services.GetRequiredService<ShellLayout>();
            layout.Navigation.IsCollapsed = true;
            layout.Details.IsVisible = false;
            shell.Regions.Browser = Services.GetRequiredService<SpikeViewModel>();
        }

        StartupClock.Mark("Shell shown");
    }
}
