using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.UseCases.Monitoring;
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

    [Fact]
    public async Task Dialog_ShowsEachMonitoringState_OnlyForAnOnlineFolder()
    {
        await app.Services.GetRequiredService<ISender>().Send(new CreateLibraryCommand("Family", app.Root), Token);
        var photos = Directory.CreateDirectory(Path.Combine(app.Root, "photos")).FullName;
        var library = app.Services.GetRequiredService<ILibrarySession>().Current!;
        await library.Folders.AddAsync(IndexedFolder.Create(library.Library.Id, photos, photos, DateTimeOffset.UtcNow), Token);
        var viewModel = app.Services.GetRequiredService<IndexedFoldersViewModel>();
        await viewModel.OpenAsync();
        var row = Assert.Single(viewModel.Folders);

        // Nothing watches in the test app, so the folder isn't monitored.
        Assert.True(row.ShowsNotMonitored);
        Assert.Equal("Not monitored", row.MonitoringText);

        row.MonitoringState = FolderMonitoringState.Watching;
        Assert.Equal(("Watching", true, false), (row.MonitoringText, row.ShowsWatching, row.ShowsNotMonitored));

        row.MonitoringState = FolderMonitoringState.Paused;
        Assert.Equal(("Paused", true, false), (row.MonitoringText, row.ShowsPaused, row.ShowsWatching));

        row.MonitoringState = FolderMonitoringState.Error;
        Assert.Equal(("Can't watch", true), (row.MonitoringText, row.ShowsMonitoringError));

        row.MonitoringState = FolderMonitoringState.Polling;
        Assert.Equal(("Polling", true, false, false), (row.MonitoringText, row.ShowsPolling, row.ShowsNotMonitored, row.ShowsManual));

        row.MonitoringState = FolderMonitoringState.Manual;
        Assert.Equal(("Manual", true, false, false), (row.MonitoringText, row.ShowsManual, row.ShowsNotMonitored, row.ShowsPolling));
    }

    [Fact]
    public async Task Dialog_PausesAndResumesMonitoring_WithItsButtonLabel()
    {
        await app.Services.GetRequiredService<ISender>().Send(new CreateLibraryCommand("Family", app.Root), Token);
        var viewModel = app.Services.GetRequiredService<IndexedFoldersViewModel>();
        var pause = app.Services.GetRequiredService<MonitoringPause>();
        await viewModel.OpenAsync();
        Assert.True(viewModel.CanPauseMonitoring);
        Assert.Equal("Pause monitoring", viewModel.MonitoringPauseLabel);

        viewModel.ToggleMonitoringPauseCommand.Execute(null);
        Assert.True(pause.IsPaused);
        Assert.Equal("Resume monitoring", viewModel.MonitoringPauseLabel);

        viewModel.ToggleMonitoringPauseCommand.Execute(null);
        Assert.False(pause.IsPaused);
        Assert.Equal("Pause monitoring", viewModel.MonitoringPauseLabel);
    }

    [Fact]
    public async Task Dialog_PartialFolder_IsTaggedAndAsksBeforeIndexingItWhole()
    {
        await app.Services.GetRequiredService<ISender>().Send(new CreateLibraryCommand("Family", app.Root), Token);
        var downloads = Directory.CreateDirectory(Path.Combine(app.Root, "downloads")).FullName;
        var library = app.Services.GetRequiredService<ILibrarySession>().Current!;
        var partial = IndexedFolder.CreateForSelectedFiles(library.Library.Id, downloads, downloads, DateTimeOffset.UtcNow);
        await library.Folders.AddAsync(partial, Token);
        var viewModel = app.Services.GetRequiredService<IndexedFoldersViewModel>();
        await viewModel.OpenAsync();

        viewModel.SelectedFolder = Assert.Single(viewModel.Folders);
        Assert.True(viewModel.SelectedFolder.IsPartial);
        viewModel.IndexWholeFolderCommand.Execute(null);
        Assert.Equal($"Index everything in {downloads}? The 0 items already in the library keep their metadata.", viewModel.Confirmation);
        Assert.Equal("Index whole folder", viewModel.ConfirmLabel);
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.False(Assert.Single(viewModel.Folders).IsPartial);
        Assert.False((await library.Folders.GetAsync(partial.Id, Token))!.SelectedFilesOnly);
    }

    [Fact]
    public async Task Dialog_AddingAPartialFolder_AsksToIndexItWhole()
    {
        await app.Services.GetRequiredService<ISender>().Send(new CreateLibraryCommand("Family", app.Root), Token);
        var downloads = Directory.CreateDirectory(Path.Combine(app.Root, "downloads")).FullName;
        var library = app.Services.GetRequiredService<ILibrarySession>().Current!;
        await library.Folders.AddAsync(IndexedFolder.CreateForSelectedFiles(library.Library.Id, downloads, downloads, DateTimeOffset.UtcNow), Token);
        var viewModel = app.Services.GetRequiredService<IndexedFoldersViewModel>();
        await viewModel.OpenAsync();

        app.Platform.Folders.Enqueue(new FolderHandle(downloads, "downloads"));
        await viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal("Index whole folder", viewModel.ConfirmLabel);
        Assert.Same(viewModel.Folders[0], viewModel.SelectedFolder);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
