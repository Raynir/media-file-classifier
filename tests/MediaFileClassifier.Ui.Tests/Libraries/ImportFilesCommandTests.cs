using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Media;
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
using MediaFileClassifier.UseCases.Import.ImportFiles;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// Tests for the Import files command (E03-S05-T02) and the message that follows it.
/// </summary>
public sealed class ImportFilesCommandTests
{
    [Fact]
    public Task ImportFiles_IsInTheLibraryMenuAfterAddFolder_WithItsShortcut() => RunAsync(open: true, (commands, _) =>
    {
        var command = commands[LibraryCommands.ImportFiles];

        Assert.Equal(CommandGroups.Library, command.Group);
        Assert.True(command.Order > commands[LibraryCommands.AddFolder].Order);
        Assert.True(command.Order < commands[LibraryCommands.ManageFolders].Order);
        Assert.Equal("Import files… (Ctrl+Shift+I)", command.ToolTip);
    });

    [Fact]
    public Task ImportFiles_WithoutALibrary_IsDisabledAndSaysWhy() => RunAsync(open: false, (commands, _) =>
    {
        var command = commands[LibraryCommands.ImportFiles];

        Assert.False(command.IsEnabled);
        Assert.Equal("Open a library first.", command.ToolTip);
    });

    [Fact]
    public Task ImportFiles_AsksForSeveralMediaFiles() => RunAsync(open: true, (commands, platform) =>
    {
        commands[LibraryCommands.ImportFiles].ExecuteAsync().GetAwaiter().GetResult();

        var request = Assert.Single(platform.FileRequests);
        Assert.Equal("Import files", request.Title);
        Assert.True(request.AllowMultiple);
        Assert.Equal(["Media files", "All files"], request.FileTypes.Select(filter => filter.Name));
        Assert.Contains("*.jpg", request.FileTypes[0].Patterns);
        Assert.Contains("*.mp4", request.FileTypes[0].Patterns);
    });

    [Fact]
    public void Describe_SaysWhatHappenedToEachKindOfFile()
    {
        static FileLocation File(string name) => new(new FolderHandle(@"C:\Downloads", "Downloads"), name);

        var text = FileImporter.Describe(TestLocalizer.EnUs(), new ImportFilesResult
        {
            Imported = [File("a.jpg"), File("b.jpg"), File("c.jpg")],
            AlreadyInLibrary = [File("d.jpg")],
            Unsupported = [File("e.txt"), File("f.txt")],
            Unreadable = [File("g.jpg")],
            ExcludedByRules = [File("h.jpg")],
        });

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "3 files are being imported.",
                "1 file is already in the library.",
                "1 file is left out by its folder's rules.",
                "2 files aren't a supported format.",
                "1 file can't be read."),
            text);
    }

    private static Task RunAsync(bool open, Action<CommandRegistry, FakePlatformServices> test) => TestApp.RunAsync(() =>
    {
        var platform = new FakePlatformServices();
        using var services = new ServiceCollection()
            .AddSingleton<IAppPaths>(new FakeAppPaths())
            .AddSingleton<IFolderLauncher>(new FakeFolderLauncher())
            .AddSingleton<ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<Infrastructure.Abstractions.Platform.IPlatformServices>(platform)
            .AddLogging()
            .AddMedia()
            .AddSingleton<Infrastructure.Abstractions.Capabilities.ICapabilityService>(new Infrastructure.Capabilities.CapabilityService([], Microsoft.Extensions.Logging.Abstractions.NullLogger<Infrastructure.Capabilities.CapabilityService>.Instance))
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
