using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// Tests for the Add folder command (E02-S03), which the toolbar's Add folder button runs (E13-S03-T02).
/// </summary>
public sealed class AddFolderCommandTests
{
    [Fact]
    public Task AddFolder_IsInTheLibraryMenu_WithItsShortcut() => RunAsync(open: true, (commands, _) =>
    {
        var command = commands[LibraryCommands.AddFolder];

        Assert.Equal(CommandGroups.Library, command.Group);
        Assert.True(command.Order > commands[LibraryCommands.OpenLibrary].Order);
        Assert.True(command.Order < commands[LibraryCommands.ManageFolders].Order);
        Assert.Equal("Icon.FolderAdd", command.IconKey);
        Assert.Equal("Add folder… (Ctrl+Shift+O)", command.ToolTip);
    });

    [Fact]
    public Task AddFolder_WithoutALibrary_IsDisabledAndSaysWhy() => RunAsync(open: false, (commands, _) =>
    {
        var command = commands[LibraryCommands.AddFolder];

        Assert.False(command.IsEnabled);
        Assert.Equal("Open a library first.", command.ToolTip);
    });

    [Fact]
    public Task AddFolder_AsksForAFolder() => RunAsync(open: true, (commands, platform) =>
    {
        platform.Folders.Enqueue(new FolderHandle(@"C:\Photos", "Photos"));

        commands[LibraryCommands.AddFolder].ExecuteAsync().GetAwaiter().GetResult();

        // The picked folder goes to the indexed folders dialog, which the headless platform can't show.
        Assert.Empty(platform.Folders);
    });

    private static Task RunAsync(bool open, Action<CommandRegistry, FakePlatformServices> test) => TestApp.RunAsync(() =>
    {
        var platform = new FakePlatformServices();
        using var services = new ServiceCollection()
            .AddSingleton<IAppPaths>(new FakeAppPaths())
            .AddSingleton<IFolderLauncher>(new FakeFolderLauncher())
            .AddSingleton<ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<Infrastructure.Abstractions.Platform.IPlatformServices>(platform)
            .AddSingleton<ISettingsStore>(new InMemorySettingsStore())
            .AddSingleton<Infrastructure.Abstractions.Jobs.IJobProgress>(new FakeJobProgress())
            .AddSingleton<Infrastructure.Abstractions.Libraries.ILibrarySession>(new FakeBrowserSession(open))
            .AddUseCases()
            .AddUi()
            .BuildServiceProvider();

        // The keymap gives the commands their shortcuts.
        services.GetRequiredService<Keymap>();
        test(services.GetRequiredService<CommandRegistry>(), platform);
    });
}
