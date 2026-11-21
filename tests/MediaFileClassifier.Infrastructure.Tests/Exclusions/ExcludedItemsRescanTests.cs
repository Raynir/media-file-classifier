using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Infrastructure.Tests.Rescan;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Exclusions;
using MediaFileClassifier.UseCases.Exclusions.ExcludeItems;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Rescan;

namespace MediaFileClassifier.Infrastructure.Tests.Exclusions;

/// <summary>
/// Items the user removed stay removed through rescans, also when their file is moved or renamed (E02-S10-T02).
/// </summary>
public sealed class ExcludedItemsRescanTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync();
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "other.png");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Theory]
    [InlineData("renamed.jpg")]
    [InlineData("sub/deeper/photo.jpg")]
    public async Task Rescan_RemovedItemThenFileMoved_StaysExcludedAtTheNewPath(string target)
    {
        var item = await ItemAsync("photo.jpg");
        await RemoveAsync(item);
        await ReconcilerTests.RescanAsync(bed);
        Assert.True(LibraryExclusions.IsRemovedByUser((await bed.Library.Items.GetAsync(item.Id, Token))!));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(bed.Root, target))!);
        File.Move(Path.Combine(bed.Root, "photo.jpg"), Path.Combine(bed.Root, target));
        await ReconcilerTests.RescanAsync(bed);

        var items = await bed.ItemsAsync();
        Assert.Equal(2, items.Count);
        var moved = Assert.Single(items, other => other.Id == item.Id);
        Assert.Equal(target, moved.Path.Value);
        Assert.True(LibraryExclusions.IsRemovedByUser(moved));
    }

    [Fact]
    public async Task Rescan_RemovedItemFileMovedToAnotherFolder_FollowsItByALibraryRescan()
    {
        var otherRoot = bed.Root + "-other";
        Directory.CreateDirectory(otherRoot);
        var other = IndexedFolder.Create(bed.Library.Library.Id, otherRoot, otherRoot, DateTimeOffset.UtcNow);
        await bed.Library.Folders.AddAsync(other, Token);
        try
        {
            var item = await ItemAsync("photo.jpg");
            await RemoveAsync(item);

            File.Move(Path.Combine(bed.Root, "photo.jpg"), Path.Combine(otherRoot, "photo.jpg"));
            await ReconcilerTests.RescanAsync(bed, library: true);

            var moved = (await bed.Library.Items.GetAsync(item.Id, Token))!;
            Assert.Equal(other.Id, moved.FolderId);
            Assert.True(LibraryExclusions.IsRemovedByUser(moved));
            Assert.Equal(2, await bed.Library.Items.CountAsync(Token));
        }
        finally
        {
            Directory.Delete(otherRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Rescan_CopyOfARemovedFile_IsImportedAsANewItem()
    {
        var item = await ItemAsync("photo.jpg");
        await RemoveAsync(item);

        File.Copy(Path.Combine(bed.Root, "photo.jpg"), Path.Combine(bed.Root, "copy.jpg"));
        await ReconcilerTests.RescanAsync(bed);

        var items = await bed.ItemsAsync();
        Assert.Equal(3, items.Count);
        Assert.True(LibraryExclusions.IsRemovedByUser(items.Single(other => other.Id == item.Id)));
        Assert.Equal("photo.jpg", items.Single(other => other.Id == item.Id).Path.Value);
        Assert.NotEqual(MediaItemStatus.Excluded, items.Single(other => other.Path.Value == "copy.jpg").Status);
    }

    [Fact]
    public async Task Reconcile_RemovedItemWithFileGoneAndNoMove_IsNeitherMissingNorMoved()
    {
        var item = await ItemAsync("photo.jpg");
        await RemoveAsync(item);
        File.Delete(Path.Combine(bed.Root, "photo.jpg"));

        var result = await bed.Get<Reconciler>().ReconcileAsync(bed.Library, [bed.Folder], Token);

        Assert.Empty(result.Missing);
        Assert.Empty(result.Moved);
        Assert.Empty(result.MovedExcluded);
    }

    [Fact]
    public async Task Reconcile_RuleExcludedItemWithFileMoved_IsNotFollowed()
    {
        var item = await ItemAsync("photo.jpg");
        item.ChangeStatus(MediaItemStatus.Excluded, DateTimeOffset.UtcNow, IndexedFolders.ExcludedByRules);
        await bed.Library.Items.UpdateAsync(item, Token);
        File.Move(Path.Combine(bed.Root, "photo.jpg"), Path.Combine(bed.Root, "renamed.jpg"));

        var result = await bed.Get<Reconciler>().ReconcileAsync(bed.Library, [bed.Folder], Token);

        Assert.Empty(result.MovedExcluded);
        Assert.Equal("renamed.jpg", Assert.Single(result.New).Path.Value);
    }

    private async Task RemoveAsync(MediaItem item)
    {
        await bed.Sender.Send(new ExcludeItemsCommand([item.Id]), Token);
        await bed.WaitUntilIdleAsync();
    }

    private async Task<MediaItem> ItemAsync(string path) => (await bed.ItemsAsync()).Single(item => item.Path.Value == path);
}
