using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Shortcuts;

/// <summary>
/// Tests for the shortcut reference overlay (E16-S01-T03).
/// </summary>
public sealed class ShortcutReferenceTests
{
    [Fact]
    public Task Reference_ListsEveryRegisteredShortcut() => RunAsync((_, viewModel, services) =>
    {
        var keymap = services.GetRequiredService<Keymap>();
        viewModel.Shortcuts.State.IsOpen = true;

        var group = Assert.Single(viewModel.Shortcuts.Groups);
        Assert.Equal("General", group.Name);
        Assert.Equal(keymap.Bindings.Select(binding => binding.CommandId).Distinct().Count(id => viewModel.Commands[id].IsVisible), group.Rows.Count);
        var help = viewModel.Commands[ShortcutCommands.ShowReference];
        Assert.Contains(group.Rows, row => row.Label == help.PlainLabel && row.Keys == "F1, Shift+/");
    });

    [Fact]
    public Task F1_OpensTheOverlay_AndEscClosesIt() => RunAsync((window, viewModel, _) =>
    {
        var overlay = window.GetVisualDescendants().OfType<ShortcutReferenceView>().Single();
        Assert.False(overlay.IsVisible);

        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(overlay.IsVisible);
        Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "F1, Shift+/");

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(overlay.IsVisible);
    });

    [Fact]
    public Task Tab_StaysInsideTheOverlay() => RunAsync((window, viewModel, _) =>
    {
        var overlay = window.GetVisualDescendants().OfType<ShortcutReferenceView>().Single();
        viewModel.Shortcuts.State.IsOpen = true;
        Dispatcher.UIThread.RunJobs();

        for (var press = 0; press < 3; press++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            var focused = Assert.IsAssignableFrom<Visual>(window.FocusManager?.GetFocusedElement());
            Assert.True(overlay.IsVisualAncestorOf(focused));
        }
    });

    [Fact]
    public Task Closing_GivesFocusBack() => RunAsync((window, _, _) =>
    {
        var button = window.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.Focusable);
        button.Focus();

        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(button, window.FocusManager?.GetFocusedElement());

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(button, window.FocusManager?.GetFocusedElement());
    });

    [Fact]
    public Task HelpMenu_OpensTheOverlay() => RunAsync((window, viewModel, _) =>
    {
        var help = viewModel.Menu.Groups.Single(group => group.Group == CommandGroups.Help);

        Assert.Contains(viewModel.Commands[ShortcutCommands.ShowReference], help.Items);
        Assert.Equal("F1", viewModel.Commands[ShortcutCommands.ShowReference].Gesture?.ToString());
    });

    private static Task RunAsync(Action<Window, MainWindowViewModel, IServiceProvider> test) => TestApp.RunAsync(() =>
    {
        using var services = new ServiceCollection()
            .AddSingleton<IAppPaths>(new FakeAppPaths())
            .AddSingleton<IFolderLauncher>(new FakeFolderLauncher())
            .AddSingleton<ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<MediaFileClassifier.Infrastructure.Abstractions.Platform.IPlatformServices>(new FakePlatformServices())
            .AddSingleton<MediaFileClassifier.Infrastructure.Abstractions.Settings.ISettingsStore>(new MediaFileClassifier.Testing.Settings.InMemorySettingsStore())
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
