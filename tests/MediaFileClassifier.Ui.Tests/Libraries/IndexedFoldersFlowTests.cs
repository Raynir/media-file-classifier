using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.UseCases.Libraries.CreateLibrary;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// End-to-end tests of the indexed folders dialog (E02-S03-T06), with real library storage in a temporary folder.
/// </summary>
public sealed class IndexedFoldersFlowTests : IAsyncDisposable
{
    private readonly TestServices app = TestServices.Build();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dialog_AddsRefusesMergesAndRemovesFolders()
    {
        await app.Services.GetRequiredService<ISender>().Send(new CreateLibraryCommand("Family", app.Root), Token);
        var photos = Directory.CreateDirectory(Path.Combine(app.Root, "media", "photos")).FullName;
        var media = Path.GetDirectoryName(photos)!;
        var viewModel = app.Services.GetRequiredService<IndexedFoldersViewModel>();
        await viewModel.OpenAsync();
        Assert.True(viewModel.IsEmpty);

        app.Platform.Folders.Enqueue(new FolderHandle(photos, "photos"));
        await viewModel.AddFolderCommand.ExecuteAsync(null);
        Assert.Equal(photos, Assert.Single(viewModel.Folders).Path);
        Assert.Equal("0 items", viewModel.Folders[0].CountText);

        // A child of an indexed folder is refused; a parent asks before merging.
        app.Platform.Folders.Enqueue(new FolderHandle(Path.Combine(photos, "2024"), "2024"));
        await viewModel.AddFolderCommand.ExecuteAsync(null);
        Assert.Equal($"{Path.Combine(photos, "2024")} is inside {photos}, which is indexed already.", viewModel.Message);

        app.Platform.Folders.Enqueue(new FolderHandle(media, "media"));
        await viewModel.AddFolderCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsConfirming);
        Assert.Equal("Merge", viewModel.ConfirmLabel);
        Assert.Single(viewModel.Folders);

        await viewModel.ConfirmCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsConfirming);
        Assert.Equal(media, Assert.Single(viewModel.Folders).Path);
        Assert.Same(viewModel.Folders[0], viewModel.SelectedFolder);

        // Exclusions: a subfolder of the selected folder is excluded, a folder outside it isn't.
        app.Platform.Folders.Enqueue(new FolderHandle(app.Root, "app.Root"));
        await viewModel.ExcludeSubfolderCommand.ExecuteAsync(null);
        Assert.Equal($"Choose a folder inside {media}.", viewModel.Message);
        app.Platform.Folders.Enqueue(new FolderHandle(photos, "photos"));
        await viewModel.ExcludeSubfolderCommand.ExecuteAsync(null);
        Assert.Equal("photos", Assert.Single(viewModel.SelectedFolder!.ExcludedSubfolders).Text);
        Assert.True(viewModel.SelectedFolder.HasRuleChanges);
        await viewModel.ApplyRulesCommand.ExecuteAsync(null);
        var stored = Assert.Single(await app.Services.GetRequiredService<ILibrarySession>().Current!.Folders.GetAllAsync(Token));
        Assert.Equal(["/photos"], stored.Rules.ExcludePatterns);
        Assert.False(viewModel.SelectedFolder!.HasRuleChanges);

        // Removing asks first, with the item count, and leaves the files alone.
        viewModel.RemoveCommand.Execute(null);
        Assert.Equal($"Remove {media} from the library? 0 items and their metadata are removed from the library. Files on disk aren't touched.", viewModel.Confirmation);
        await viewModel.ConfirmCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsEmpty);
        Assert.True(Directory.Exists(photos));
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
