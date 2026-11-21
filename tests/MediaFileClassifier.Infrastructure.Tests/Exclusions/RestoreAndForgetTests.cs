using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Infrastructure.Tests.Rescan;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Exclusions;
using MediaFileClassifier.UseCases.Exclusions.ExcludeItems;
using MediaFileClassifier.UseCases.Exclusions.ForgetExcludedItems;
using MediaFileClassifier.UseCases.Exclusions.GetExcludedItems;
using MediaFileClassifier.UseCases.Exclusions.RestoreExcludedItems;
using MediaFileClassifier.UseCases.Libraries;

namespace MediaFileClassifier.Infrastructure.Tests.Exclusions;

/// <summary>
/// Listing, restoring and forgetting the items the user removed, with a real library, import and job system
/// (E02-S10-T04).
/// </summary>
public sealed class RestoreAndForgetTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync();
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "alpha.png");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task GetExcludedItems_ListsOnlyUserRemovedItems_WithTheirFolderPath()
    {
        var photo = await ItemAsync("a/photo.jpg");
        var alpha = await ItemAsync("alpha.png");
        await RemoveAsync(photo);
        alpha.ChangeStatus(MediaItemStatus.Excluded, DateTimeOffset.UtcNow, IndexedFolders.ExcludedByRules);
        await bed.Library.Items.UpdateAsync(alpha, Token);

        var listed = Assert.Single(await bed.Sender.Send(new GetExcludedItemsQuery(), Token));

        Assert.Equal(photo.Id, listed.Id);
        Assert.Equal("photo.jpg", listed.FileName);
        Assert.Equal(Path.Combine(bed.Folder.DisplayPath, "a"), listed.FolderPath);
    }

    [Fact]
    public async Task Restore_FileStillThere_ImportsItAgain_WithItsUserValuesAndThumbnail()
    {
        var item = await ItemAsync("a/photo.jpg");
        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.AddTag(TagName.From("holiday"), DateTimeOffset.UtcNow), Token);
        await bed.Library.Items.SetFavoriteAsync([item.Id], true, DateTimeOffset.UtcNow, Token);
        await RemoveAsync(item);
        var thumbnail = bed.Library.Layout.GetThumbnailPath(item.Id, ThumbnailSize.Small);
        Assert.False(File.Exists(thumbnail));

        var result = await bed.Sender.Send(new RestoreExcludedItemsCommand([item.Id]), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(new RestoreExcludedItemsResult(1, 0, 0), result);
        var restored = (await bed.Library.Items.GetAsync(item.Id, Token))!;
        Assert.NotEqual(MediaItemStatus.Excluded, restored.Status);
        Assert.True(restored.IsFavorite);
        Assert.Contains((await bed.Library.Metadata.GetAsync(item.Id, Token)).Tags, tag => tag.Tag.Value == "holiday");
        Assert.True(File.Exists(thumbnail));
        Assert.Empty(await bed.Sender.Send(new GetExcludedItemsQuery(), Token));
        Assert.Equal(2, await bed.Library.Items.CountAsync(Token));
    }

    [Fact]
    public async Task Restore_FileGone_MakesTheItemMissing()
    {
        var item = await ItemAsync("a/photo.jpg");
        await RemoveAsync(item);
        File.Delete(Path.Combine(bed.Root, "a/photo.jpg"));

        var result = await bed.Sender.Send(new RestoreExcludedItemsCommand([item.Id]), Token);

        Assert.Equal(new RestoreExcludedItemsResult(0, 1, 0), result);
        Assert.Equal(MediaItemStatus.Missing, (await bed.Library.Items.GetAsync(item.Id, Token))!.Status);
    }

    [Fact]
    public async Task Restore_FileGoneThenBack_TheRescanMakesItsThumbnailAgain()
    {
        var item = await ItemAsync("a/photo.jpg");
        await RemoveAsync(item);
        var file = Path.Combine(bed.Root, "a/photo.jpg");
        var away = Path.Combine(bed.Root + "-away.jpg");
        File.Move(file, away);
        await bed.Sender.Send(new RestoreExcludedItemsCommand([item.Id]), Token);

        File.Move(away, file);
        await ReconcilerTests.RescanAsync(bed);

        var back = (await bed.Library.Items.GetAsync(item.Id, Token))!;
        Assert.Equal(MediaItemStatus.Pending, back.Status);
        Assert.True(File.Exists(bed.Library.Layout.GetThumbnailPath(item.Id, ThumbnailSize.Small)));
    }

    [Fact]
    public async Task RestoreRequestedItem_IsStillListed_AndCanBeForgotten()
    {
        // As left by a restore whose import failed: the item mustn't become unreachable.
        var item = await ItemAsync("a/photo.jpg");
        await RemoveAsync(item);
        var stored = (await bed.Library.Items.GetAsync(item.Id, Token))!;
        stored.ChangeExclusionReason(LibraryExclusions.RestoreRequested);
        await bed.Library.Items.UpdateAsync(stored, Token);

        Assert.Equal(item.Id, Assert.Single(await bed.Sender.Send(new GetExcludedItemsQuery(), Token)).Id);
        Assert.Equal(1, await bed.Sender.Send(new ForgetExcludedItemsCommand([item.Id]), Token));
        Assert.Null(await bed.Library.Items.GetAsync(item.Id, Token));
    }

    [Fact]
    public async Task Restore_FileChangedWhileRemoved_ImportsTheNewContent()
    {
        var item = await ItemAsync("alpha.png");
        await RemoveAsync(item);
        File.Copy(SampleMedia.JpegWithExif, Path.Combine(bed.Root, "alpha.png"), overwrite: true);

        await bed.Sender.Send(new RestoreExcludedItemsCommand([item.Id]), Token);
        await bed.WaitUntilIdleAsync();

        var restored = (await bed.Library.Items.GetAsync(item.Id, Token))!;
        Assert.NotEqual(MediaItemStatus.Excluded, restored.Status);
        Assert.Equal(new FileInfo(Path.Combine(bed.Root, "alpha.png")).Length, restored.SizeBytes);
        Assert.NotEqual(item.Hashes.Fingerprint, restored.Hashes.Fingerprint);
    }

    [Fact]
    public async Task RestoreAndForget_SkipItemsTheRulesExcluded()
    {
        var item = await ItemAsync("a/photo.jpg");
        item.ChangeStatus(MediaItemStatus.Excluded, DateTimeOffset.UtcNow, IndexedFolders.ExcludedByRules);
        await bed.Library.Items.UpdateAsync(item, Token);

        Assert.Equal(new RestoreExcludedItemsResult(0, 0, 0), await bed.Sender.Send(new RestoreExcludedItemsCommand([item.Id]), Token));
        Assert.Equal(0, await bed.Sender.Send(new ForgetExcludedItemsCommand([item.Id]), Token));
        Assert.Equal(IndexedFolders.ExcludedByRules, (await bed.Library.Items.GetAsync(item.Id, Token))!.StatusReason);
    }

    [Fact]
    public async Task Forget_DeletesTheItem_AndTheNextRescanImportsTheFileAsANewItem()
    {
        var item = await ItemAsync("a/photo.jpg");
        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.AddTag(TagName.From("holiday"), DateTimeOffset.UtcNow), Token);
        await RemoveAsync(item);

        Assert.Equal(1, await bed.Sender.Send(new ForgetExcludedItemsCommand([item.Id]), Token));

        Assert.Null(await bed.Library.Items.GetAsync(item.Id, Token));
        Assert.Empty(await bed.Sender.Send(new GetExcludedItemsQuery(), Token));
        Assert.True(File.Exists(Path.Combine(bed.Root, "a/photo.jpg")));

        await ReconcilerTests.RescanAsync(bed);
        var again = await ItemAsync("a/photo.jpg");
        Assert.NotEqual(item.Id, again.Id);
        Assert.NotEqual(MediaItemStatus.Excluded, again.Status);
        Assert.DoesNotContain((await bed.Library.Metadata.GetAsync(again.Id, Token)).Tags, tag => tag.Tag.Value == "holiday");
    }

    private async Task RemoveAsync(MediaItem item)
    {
        await bed.Sender.Send(new ExcludeItemsCommand([item.Id]), Token);
        await bed.WaitUntilIdleAsync();
        Assert.True(LibraryExclusions.IsRemovedByUser((await bed.Library.Items.GetAsync(item.Id, Token))!));
    }

    private async Task<MediaItem> ItemAsync(string path) => (await bed.ItemsAsync()).Single(item => item.Path.Value == path);
}
