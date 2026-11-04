using MediaFileClassifier.Infrastructure;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.DataAccess;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.UseCases.Libraries.CreateLibrary;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// End-to-end tests of the indexed folders dialog (E02-S03-T06), with real library storage in a temporary folder.
/// </summary>
public sealed class IndexedFoldersFlowTests : IAsyncDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly Commands.FakePlatformServices platform;
    private readonly ServiceProvider services;

    public IndexedFoldersFlowTests()
    {
        platform = new Commands.FakePlatformServices(new AppPaths(Path.Combine(root, "app")));
        services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IAppPaths>(platform.Paths)
            .AddSingleton<IPlatformServices>(platform)
            .AddSingleton<Platform.ICapabilityGate>(new Commands.FakeCapabilityGate())
            .AddInfrastructure()
            .AddDataAccess()
            .AddSingleton<IMediaFileSource, FileSystemMediaFileSource>()
            .AddSingleton<IThumbnailCache, NoThumbnails>()
            .AddUseCases()
            .AddUi()
            .BuildServiceProvider();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dialog_AddsRefusesMergesAndRemovesFolders()
    {
        await services.GetRequiredService<ISender>().Send(new CreateLibraryCommand("Family", root), Token);
        var photos = Directory.CreateDirectory(Path.Combine(root, "media", "photos")).FullName;
        var media = Path.GetDirectoryName(photos)!;
        var viewModel = services.GetRequiredService<IndexedFoldersViewModel>();
        await viewModel.OpenAsync();
        Assert.True(viewModel.IsEmpty);

        platform.Folders.Enqueue(new FolderHandle(photos, "photos"));
        await viewModel.AddFolderCommand.ExecuteAsync(null);
        Assert.Equal(photos, Assert.Single(viewModel.Folders).Path);
        Assert.Equal("0 items", viewModel.Folders[0].CountText);

        // A child of an indexed folder is refused; a parent asks before merging.
        platform.Folders.Enqueue(new FolderHandle(Path.Combine(photos, "2024"), "2024"));
        await viewModel.AddFolderCommand.ExecuteAsync(null);
        Assert.Equal($"{Path.Combine(photos, "2024")} is inside {photos}, which is indexed already.", viewModel.Message);

        platform.Folders.Enqueue(new FolderHandle(media, "media"));
        await viewModel.AddFolderCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsConfirming);
        Assert.Equal("Merge", viewModel.ConfirmLabel);
        Assert.Single(viewModel.Folders);

        await viewModel.ConfirmCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsConfirming);
        Assert.Equal(media, Assert.Single(viewModel.Folders).Path);
        Assert.Same(viewModel.Folders[0], viewModel.SelectedFolder);

        // Exclusions: a subfolder of the selected folder is excluded, a folder outside it isn't.
        platform.Folders.Enqueue(new FolderHandle(root, "root"));
        await viewModel.ExcludeSubfolderCommand.ExecuteAsync(null);
        Assert.Equal($"Choose a folder inside {media}.", viewModel.Message);
        platform.Folders.Enqueue(new FolderHandle(photos, "photos"));
        await viewModel.ExcludeSubfolderCommand.ExecuteAsync(null);
        Assert.Equal("photos", Assert.Single(viewModel.SelectedFolder!.ExcludedSubfolders).Text);
        Assert.True(viewModel.SelectedFolder.HasRuleChanges);
        await viewModel.ApplyRulesCommand.ExecuteAsync(null);
        var stored = Assert.Single(await services.GetRequiredService<ILibrarySession>().Current!.Folders.GetAllAsync(Token));
        Assert.Equal(["/photos"], stored.Rules.ExcludePatterns);
        Assert.False(viewModel.SelectedFolder!.HasRuleChanges);

        // Removing asks first, with the item count, and leaves the files alone.
        viewModel.RemoveCommand.Execute(null);
        Assert.Equal($"Remove {media} from the library? 0 items and their metadata are removed from the library. Files on disk aren't touched.", viewModel.Confirmation);
        await viewModel.ConfirmCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsEmpty);
        Assert.True(Directory.Exists(photos));
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
}

/// <summary>
/// A thumbnail cache with nothing in it.
/// </summary>
internal sealed class NoThumbnails : IThumbnailCache
{
    public string? Find(LibraryLayout library, Id<MediaItem> itemId, ThumbnailSize size, DateTimeOffset sourceModifiedAt) => null;

    public Task<ThumbnailOutcome> GenerateAsync(LibraryLayout library, MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public void Invalidate(LibraryLayout library, Id<MediaItem> itemId)
    {
    }
}
