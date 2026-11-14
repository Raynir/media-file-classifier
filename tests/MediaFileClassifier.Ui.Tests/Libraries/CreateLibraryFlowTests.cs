using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// End-to-end tests of the welcome screen and the create-library dialog (E02-S01-T02, T03), with real library storage
/// in a temporary folder.
/// </summary>
public sealed class CreateLibraryFlowTests : IAsyncDisposable
{
    private readonly TestServices app = TestServices.Build(fullStack: true);

    [Fact]
    public Task FirstLaunch_ShowsTheWelcomeScreen() => TestApp.RunAsync(() =>
    {
        app.Services.GetRequiredService<LibraryShell>();

        Assert.IsType<WelcomeViewModel>(app.Services.GetRequiredService<ShellRegions>().Browser);
        Assert.Null(app.Services.GetRequiredService<ShellRegions>().Toolbar);
    });

    [Fact]
    public async Task Dialog_CreatesTheLibraryAndHandsOverToAddingFolders()
    {
        var viewModel = app.Services.GetRequiredService<CreateLibraryViewModel>();
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;
        await viewModel.ValidateAsync();
        Assert.Null(viewModel.Error);
        Assert.False(viewModel.CreateCommand.CanExecute(null));

        app.Platform.Folders.Enqueue(new FolderHandle(Path.Combine(app.Root, "libraries"), "libraries"));
        await viewModel.BrowseCommand.ExecuteAsync(null);
        viewModel.Name = "Family";
        await viewModel.ValidateAsync();
        await viewModel.CreateCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsFoldersStep, viewModel.Error);
        var session = app.Services.GetRequiredService<ILibrarySession>();
        Assert.Equal(Path.Combine(app.Root, "libraries", "Family.mfc"), session.Current?.Layout.RootDirectory);
        Assert.Equal("Family", Assert.Single(app.Services.GetRequiredService<ISettingsService>().Current.RecentLibraries!).Name);

        var photos = Directory.CreateDirectory(Path.Combine(app.Root, "Photos")).FullName;
        app.Platform.Folders.Enqueue(new FolderHandle(photos, "Photos"));
        await viewModel.AddFolderCommand.ExecuteAsync(null);
        viewModel.CloseCommand.Execute(null);

        Assert.Equal([photos], viewModel.Folders);
        Assert.Equal(photos, Assert.Single(await session.Current!.Folders.GetAllAsync(TestContext.Current.CancellationToken)).PlatformHandle);
        Assert.True(closed);
    }

    [Fact]
    public async Task Dialog_ShowsWhyALocationIsRejected()
    {
        var viewModel = app.Services.GetRequiredService<CreateLibraryViewModel>();
        Directory.CreateDirectory(Path.Combine(app.Root, "Family.mfc", "thumbnails"));

        viewModel.Location = Path.Combine(app.Root, "Family.mfc");
        viewModel.Name = "Trips";
        await viewModel.ValidateAsync();

        Assert.Equal("This folder is inside another library. Choose a folder outside it.", viewModel.Error);
        Assert.False(viewModel.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpeningALibrary_ShowsTheThumbnailGridAndToolbarInsteadOfTheWelcomeScreen()
    {
        await TestApp.RunAsync(() => app.Services.GetRequiredService<LibraryShell>());
        var library = await app.Services.GetRequiredService<ILibraryStore>().CreateAsync(app.Root, "Family", TestContext.Current.CancellationToken);

        await TestApp.RunAsync(() => app.Services.GetRequiredService<ILibrarySession>().SetCurrentAsync(library).GetAwaiter().GetResult());

        Assert.IsType<ThumbnailGridViewModel>(app.Services.GetRequiredService<ShellRegions>().Browser);
        Assert.IsType<Ui.Toolbar.ToolbarViewModel>(app.Services.GetRequiredService<ShellRegions>().Toolbar);
    }

    [Fact]
    public async Task Startup_ReopensTheLastLibrary()
    {
        var sender = app.Services.GetRequiredService<MediatR.ISender>();
        var session = app.Services.GetRequiredService<ILibrarySession>();
        await sender.Send(new UseCases.Libraries.CreateLibrary.CreateLibraryCommand("Family", app.Root), TestContext.Current.CancellationToken);
        await session.SetCurrentAsync(null);

        await app.Services.GetRequiredService<LibraryOpener>().OpenLastAsync();

        Assert.Equal(Path.Combine(app.Root, "Family.mfc"), session.Current?.Layout.RootDirectory);
    }

    [Fact]
    public async Task Startup_MissingLastLibrary_ExplainsOnTheWelcomeScreen()
    {
        var sender = app.Services.GetRequiredService<MediatR.ISender>();
        await sender.Send(new UseCases.Libraries.CreateLibrary.CreateLibraryCommand("Family", app.Root), TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<ILibrarySession>().SetCurrentAsync(null);
        Directory.Move(Path.Combine(app.Root, "Family.mfc"), Path.Combine(app.Root, "Moved.mfc"));
        await TestApp.RunAsync(() => app.Services.GetRequiredService<LibraryShell>());
        var welcome = app.Services.GetRequiredService<WelcomeViewModel>();

        await app.Services.GetRequiredService<LibraryOpener>().OpenLastAsync();
        await welcome.RefreshAsync();

        Assert.Equal("Family couldn't be opened. The library folder or its database is missing.", welcome.Message);
        var recent = Assert.Single(welcome.Recent);
        Assert.False(recent.IsAvailable);
        Assert.False(recent.OpenCommand.CanExecute(null));
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
