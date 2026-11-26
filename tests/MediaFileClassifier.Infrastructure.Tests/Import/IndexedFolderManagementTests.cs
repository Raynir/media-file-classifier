using System.Security.Cryptography;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.UseCases.Import.ImportFolders;
using MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;
using MediaFileClassifier.UseCases.Libraries.ChangeFolderRules;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using MediaFileClassifier.UseCases.Libraries.GetIndexedFolders;
using MediaFileClassifier.UseCases.Libraries.RemoveFolder;
using MediaFileClassifier.UseCases.Monitoring;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// End-to-end tests of managing indexed folders (E02-S03) on real files, a real library and the real job system.
/// </summary>
public sealed class IndexedFolderManagementTests : IAsyncLifetime
{
    private readonly TemporaryFolder temp = new();
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The bed's folder is a child of a folder of the test's own, so a parent folder can be added too.
    private string Child => temp.Combine("child");

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync(root: Child);

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        temp.Dispose();
    }

    [Fact]
    public async Task AddFolder_ImportsItsItems()
    {
        var other = Directory.CreateDirectory(temp.Combine("other")).FullName;
        File.Copy(Testing.Media.SampleMedia.JpegWithExif, Path.Combine(other, "a.jpg"));

        var result = await bed.Sender.Send(new AddLibraryFoldersCommand([new FolderHandle(other, "other")]), Token);
        await bed.WaitUntilIdleAsync();

        var folder = Assert.Single(result.Added);
        var item = Assert.Single(await bed.Library.Items.GetByFolderAsync(folder.Id, Token));
        Assert.Equal(MediaItemStatus.Pending, item.Status);
    }

    [Fact]
    public async Task AddFolder_InsideAnIndexedFolder_IsRefused()
    {
        var sub = Directory.CreateDirectory(Path.Combine(Child, "2024")).FullName;

        var result = await bed.Sender.Send(new AddLibraryFoldersCommand([new FolderHandle(sub, "2024"), new FolderHandle(Child, "child")]), Token);

        Assert.Empty(result.Added);
        Assert.All(result.Refused, refused => Assert.Equal(bed.Folder.Id, refused.CoveredBy.Id));
        Assert.Equal(2, result.Refused.Count);
    }

    [Fact]
    public async Task AddParent_MergesTheChildKeepingItsItemsAndMetadata()
    {
        bed.WriteDistinctPhotos(3);
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var before = await bed.ItemsAsync();
        var favorite = before[0];
        favorite.SetFavorite(true, LibraryTestBedStart);
        await bed.Library.Items.UpdateAsync(favorite, Token);
        await bed.Library.Metadata.AddTagAsync([favorite.Id], TagName.From("beach"), Token);
        var child = (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!;
        child.ChangeRules(new FolderRules { ExcludePatterns = [FolderRules.ForSubfolder("raw")] });
        await bed.Library.Folders.UpdateAsync(child, Token);

        var result = await bed.Sender.Send(new AddLibraryFoldersCommand([new FolderHandle(temp.Path, "parent")]), Token);
        await bed.WaitUntilIdleAsync();

        var added = Assert.Single(result.Added);
        Assert.Equal(bed.Folder.Id, Assert.Single(result.Merged).Id);
        Assert.Null(await bed.Library.Folders.GetAsync(bed.Folder.Id, Token));
        Assert.Equal(["/child/raw"], (await bed.Library.Folders.GetAsync(added.Id, Token))!.Rules.ExcludePatterns);

        // The same items, not re-created: same IDs, paths relative to the parent, values and metadata kept.
        var after = await bed.Library.Items.GetByFolderAsync(added.Id, Token);
        Assert.Equal(before.Select(item => item.Id.ToString()).Order(), after.Select(item => item.Id.ToString()).Order());
        Assert.All(after, item => Assert.StartsWith("child/photos/", item.Path.Value, StringComparison.Ordinal));
        Assert.All(after, item => Assert.Equal(MediaItemStatus.Pending, item.Status));
        Assert.True(after.Single(item => item.Id == favorite.Id).IsFavorite);
        Assert.Contains((await bed.Library.Metadata.GetAsync(favorite.Id, Token)).Tags, tag => tag.Tag == TagName.From("beach"));
        Assert.All(after, item => Assert.NotNull(item.Hashes.ContentHash));
        foreach (var item in after)
        {
            Assert.NotNull(await bed.Library.Items.GetTechnicalMetadataAsync(item.Id, Token));
        }
    }

    [Fact]
    public async Task ExcludeSubfolder_MarksItsItemsExcluded_AndRemovingTheExclusionBringsThemBack()
    {
        bed.WriteDistinctPhotos(2, "keep");
        bed.WriteDistinctPhotos(2, "raw");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var raw = (await bed.ItemsAsync()).Where(item => item.Path.Value.StartsWith("raw/", StringComparison.Ordinal)).ToList();
        raw[0].SetFavorite(true, LibraryTestBedStart);
        await bed.Library.Items.UpdateAsync(raw[0], Token);

        var excluded = await bed.Sender.Send(new ChangeFolderRulesCommand(bed.Folder.Id, new FolderRules { ExcludePatterns = [FolderRules.ForSubfolder("raw")] }), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(new ChangeFolderRulesResult(Excluded: 2, Restored: 0), excluded);
        var items = await bed.ItemsAsync();
        Assert.All(items, item => Assert.Equal(item.Path.Value.StartsWith("raw/", StringComparison.Ordinal) ? MediaItemStatus.Excluded : MediaItemStatus.Pending, item.Status));
        Assert.True(items.Single(item => item.Id == raw[0].Id).IsFavorite);
        Assert.All(raw, item => Assert.False(File.Exists(bed.Library.Layout.GetThumbnailPath(item.Id, ThumbnailSize.Small))));
        var summary = Assert.Single(await bed.Sender.Send(new GetIndexedFoldersQuery(), Token), folder => folder.Folder.Id == bed.Folder.Id);
        Assert.Equal((2, 2), (summary.ItemCount, summary.ExcludedCount));

        // An item the user excluded isn't the rules' to bring back (D024).
        var removedByUser = items.First(item => item.Status == MediaItemStatus.Pending);
        removedByUser.ChangeStatus(MediaItemStatus.Excluded, LibraryTestBedStart, "Removed by the user.");
        await bed.Library.Items.UpdateAsync(removedByUser, Token);

        var restored = await bed.Sender.Send(new ChangeFolderRulesCommand(bed.Folder.Id, new FolderRules()), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(new ChangeFolderRulesResult(Excluded: 0, Restored: 2), restored);
        items = await bed.ItemsAsync();
        Assert.Equal(4, items.Count);
        Assert.All(items, item => Assert.Equal(item.Id == removedByUser.Id ? MediaItemStatus.Excluded : MediaItemStatus.Pending, item.Status));
        Assert.True(items.Single(item => item.Id == raw[0].Id).IsFavorite);
        Assert.All(raw, item => Assert.True(File.Exists(bed.Library.Layout.GetThumbnailPath(item.Id, ThumbnailSize.Small))));
    }

    [Fact]
    public async Task IncludeHidden_ImportsTheHiddenFiles()
    {
        bed.WriteDistinctPhotos(1);
        var hidden = bed.Copy(Testing.Media.SampleMedia.JpegWithExif, "hidden.jpg");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        Assert.Single(await bed.ItemsAsync());

        await bed.Sender.Send(new ChangeFolderRulesCommand(bed.Folder.Id, new FolderRules { IncludeHidden = true }), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Contains(await bed.ItemsAsync(), item => item.Path.Value == "hidden.jpg");
    }

    [Fact]
    public async Task RemoveFolder_DeletesItsItemsAndThumbnails_AndLeavesTheFilesUntouched()
    {
        bed.WriteDistinctPhotos(3);
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var items = await bed.ItemsAsync();
        var files = Directory.EnumerateFiles(Child, "*", SearchOption.AllDirectories).ToDictionary(file => file, file => SHA256.HashData(File.ReadAllBytes(file)));

        Assert.True(await bed.Sender.Send(new RemoveFolderCommand(bed.Folder.Id), Token));
        Assert.DoesNotContain(await bed.Sender.Send(new GetIndexedFoldersQuery(), Token), folder => folder.Folder.Id == bed.Folder.Id);
        await bed.WaitUntilIdleAsync();

        Assert.Null(await bed.Library.Folders.GetAsync(bed.Folder.Id, Token));
        Assert.Empty(await bed.Library.Items.GetByFolderAsync(bed.Folder.Id, Token));
        Assert.All(items, item => Assert.False(File.Exists(bed.Library.Layout.GetThumbnailPath(item.Id, ThumbnailSize.Small))));
        Assert.Equal(files.Keys.Order(), Directory.EnumerateFiles(Child, "*", SearchOption.AllDirectories).Order());
        Assert.All(files, file => Assert.Equal(file.Value, SHA256.HashData(File.ReadAllBytes(file.Key))));
    }

    [Fact]
    public async Task UnpluggedFolder_IsOfflineWithItsItemsIntact_AndOnlineAgainWhenBack()
    {
        bed.WriteDistinctPhotos(2);
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var before = await bed.ItemsAsync();
        var unplugged = Child + "-unplugged";

        Directory.Move(Child, unplugged);
        var offline = await bed.Sender.Send(new CheckFolderAvailabilityCommand([bed.Folder.Id]), Token);
        var importWhileOffline = await bed.Sender.Send(new ImportFoldersCommand([bed.Folder.Id]), Token);

        Assert.Equal(FolderStatus.Offline, Assert.Single(offline).Status);
        Assert.Empty(importWhileOffline);
        Assert.Equal(IndexedFolderState.Offline, (await bed.Sender.Send(new GetIndexedFoldersQuery(), Token)).Single(folder => folder.Folder.Id == bed.Folder.Id).State);
        await AssertUnchangedAsync(before);

        Directory.Move(unplugged, Child);
        var online = await bed.Sender.Send(new CheckFolderAvailabilityCommand([bed.Folder.Id]), Token);

        // Back Online, the folder gets a quick sync that starts in the background (E04-S04-T02). Ending the test while
        // it runs would close and delete the library under it (MFC-1059).
        await bed.Get<ReconnectCatchUp>().WaitAsync();
        await bed.WaitUntilIdleAsync();

        Assert.Equal(FolderStatus.Online, Assert.Single(online).Status);
        await AssertUnchangedAsync(before);
    }

    private static DateTimeOffset LibraryTestBedStart => DataAccess.LibraryTestBed.Start;

    private async Task AssertUnchangedAsync(IReadOnlyList<MediaItem> before)
    {
        var after = (await bed.ItemsAsync()).ToDictionary(item => item.Id);
        Assert.Equal(before.Count, after.Count);
        foreach (var item in before)
        {
            Assert.Equal((item.Path, item.Status, item.Hashes), (after[item.Id].Path, after[item.Id].Status, after[item.Id].Hashes));
            Assert.NotNull(await bed.Library.Items.GetTechnicalMetadataAsync(item.Id, Token));
        }
    }
}
