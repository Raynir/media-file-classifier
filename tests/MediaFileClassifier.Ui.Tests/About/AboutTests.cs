using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Ui.About;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.Views;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.About;

/// <summary>
/// Tests for Help › About (MFC-1023).
/// </summary>
public sealed class AboutTests
{
    [Fact]
    public Task AboutCommand_IsInTheHelpMenu_AndAlwaysEnabled() => RunAsync((_, viewModel, _) =>
    {
        var about = viewModel.Commands[AboutCommands.ShowAbout];

        Assert.Contains(about, viewModel.Menu.Groups.Single(group => group.Group == CommandGroups.Help).Items);
        Assert.True(about.IsEnabled);
    });

    [Fact]
    public Task AboutCommand_OpensTheOverlayWithTheVersion_AndEscCloses() => RunAsync((window, viewModel, _) =>
    {
        var overlay = window.GetVisualDescendants().OfType<AboutView>().Single();
        Assert.False(overlay.IsVisible);

        viewModel.Commands[AboutCommands.ShowAbout].ExecuteAsync().GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.True(overlay.IsVisible);
        Assert.Contains(overlay.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text == AppVersion.Current.Text);
        Assert.Contains(overlay.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text == AboutViewModel.RepositoryUrl);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(overlay.IsVisible);
    });

    private static Task RunAsync(Action<Window, MainWindowViewModel, IServiceProvider> test) => TestApp.RunAsync(() =>
    {
        using var services = new ServiceCollection()
            .AddSingleton<IAppPaths>(new FakeAppPaths())
            .AddSingleton<IFolderLauncher>(new FakeFolderLauncher())
            .AddSingleton<ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<MediaFileClassifier.Infrastructure.Abstractions.Platform.IPlatformServices>(new FakePlatformServices())
            .AddSingleton<MediaFileClassifier.Infrastructure.Abstractions.Settings.ISettingsStore>(new MediaFileClassifier.Testing.Settings.InMemorySettingsStore())
            .AddSingleton<Infrastructure.Abstractions.Jobs.IJobProgress>(new FakeJobProgress())
            .AddSingleton<Infrastructure.Abstractions.Libraries.ILibrarySession>(new Browser.FakeBrowserSession(open: false))
            .AddUseCases()
            .AddUi()
            .BuildServiceProvider();
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        services.GetRequiredService<ShortcutHandler>().Attach(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            test(window, viewModel, services);
        }
        finally
        {
            window.Close();
        }
    });
}
