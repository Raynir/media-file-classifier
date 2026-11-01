using MediaFileClassifier.Infrastructure;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.DataAccess;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// End-to-end tests of the welcome screen and the create-library dialog (E02-S01-T02, T03), with real library storage
/// in a temporary folder.
/// </summary>
public sealed class CreateLibraryFlowTests : IAsyncDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly FakePlatform platform;
    private readonly ServiceProvider services;

    public CreateLibraryFlowTests()
    {
        platform = new FakePlatform(new AppPaths(Path.Combine(root, "app")));
        services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IAppPaths>(platform.Paths)
            .AddSingleton<IPlatformServices>(platform)
            .AddSingleton<Platform.ICapabilityGate>(new Commands.FakeCapabilityGate())
            .AddInfrastructure()
            .AddDataAccess()
            .AddUseCases()
            .AddUi()
            .BuildServiceProvider();
    }

    [Fact]
    public Task FirstLaunch_ShowsTheWelcomeScreen() => TestApp.RunAsync(() =>
    {
        services.GetRequiredService<LibraryShell>();

        Assert.IsType<WelcomeViewModel>(services.GetRequiredService<ShellRegions>().Browser);
    });

    [Fact]
    public async Task Dialog_CreatesTheLibraryAndHandsOverToAddingFolders()
    {
        var viewModel = services.GetRequiredService<CreateLibraryViewModel>();
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;
        await viewModel.ValidateAsync();
        Assert.Null(viewModel.Error);
        Assert.False(viewModel.CreateCommand.CanExecute(null));

        platform.Folders.Enqueue(new FolderHandle(Path.Combine(root, "libraries"), "libraries"));
        await viewModel.BrowseCommand.ExecuteAsync(null);
        viewModel.Name = "Family";
        await viewModel.ValidateAsync();
        await viewModel.CreateCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsFoldersStep, viewModel.Error);
        var session = services.GetRequiredService<ILibrarySession>();
        Assert.Equal(Path.Combine(root, "libraries", "Family.mfc"), session.Current?.Layout.RootDirectory);
        Assert.Equal("Family", Assert.Single(services.GetRequiredService<ISettingsService>().Current.RecentLibraries!).Name);

        var photos = Directory.CreateDirectory(Path.Combine(root, "Photos")).FullName;
        platform.Folders.Enqueue(new FolderHandle(photos, "Photos"));
        await viewModel.AddFolderCommand.ExecuteAsync(null);
        viewModel.CloseCommand.Execute(null);

        Assert.Equal([photos], viewModel.Folders);
        Assert.Equal(photos, Assert.Single(await session.Current!.Folders.GetAllAsync(TestContext.Current.CancellationToken)).PlatformHandle);
        Assert.True(closed);
    }

    [Fact]
    public async Task Dialog_ShowsWhyALocationIsRejected()
    {
        var viewModel = services.GetRequiredService<CreateLibraryViewModel>();
        Directory.CreateDirectory(Path.Combine(root, "Family.mfc", "thumbnails"));

        viewModel.Location = Path.Combine(root, "Family.mfc");
        viewModel.Name = "Trips";
        await viewModel.ValidateAsync();

        Assert.Equal("This folder is inside another library. Choose a folder outside it.", viewModel.Error);
        Assert.False(viewModel.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpeningALibrary_ReplacesTheWelcomeScreen()
    {
        await TestApp.RunAsync(() => services.GetRequiredService<LibraryShell>());
        var library = await services.GetRequiredService<ILibraryStore>().CreateAsync(root, "Family", TestContext.Current.CancellationToken);

        await TestApp.RunAsync(() => services.GetRequiredService<ILibrarySession>().SetCurrentAsync(library).GetAwaiter().GetResult());

        Assert.Null(services.GetRequiredService<ShellRegions>().Browser);
    }

    public async ValueTask DisposeAsync()
    {
        await services.GetRequiredService<ILibrarySession>().SetCurrentAsync(null);
        await services.DisposeAsync();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakePlatform(IAppPaths paths) : IPlatformServices
    {
        public Queue<FolderHandle> Folders { get; } = [];

        public IAppPaths Paths { get; } = paths;

        public bool CanRevealInFileExplorer => false;

        public Task<FolderHandle?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
            => Task.FromResult(Folders.TryDequeue(out var folder) ? folder : null);

        public Task<IReadOnlyList<FileLocation>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileLocation>>([]);

        public Task RevealInFileExplorerAsync(FileLocation location, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task OpenWithDefaultAppAsync(FileLocation location, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
