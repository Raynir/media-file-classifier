using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Rescan;

namespace MediaFileClassifier.Infrastructure.Tests.Rescan;

/// <summary>
/// Moved and renamed files keep their items (E02-S04-T02).
/// </summary>
public sealed class MoveDetectionTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync();

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Theory]
    [InlineData("renamed.jpg")]
    [InlineData("sub/deeper/photo.jpg")]
    public async Task Rescan_RenamedOrMovedFile_KeepsItsItemAndMetadata(string target)
    {
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "other.png");
        await ImportAsync();
        var item = await ItemAsync("photo.jpg");
        await TagAsync(item);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(bed.Root, target))!);
        File.Move(Path.Combine(bed.Root, "photo.jpg"), Path.Combine(bed.Root, target));
        await ReconcilerTests.RescanAsync(bed);

        var items = await bed.ItemsAsync();
        Assert.Equal(2, items.Count);
        var moved = Assert.Single(items, other => other.Id == item.Id);
        Assert.Equal(target, moved.Path.Value);
        Assert.Equal(MediaItemStatus.Pending, moved.Status);
        Assert.True(moved.IsFavorite);
        Assert.Contains((await bed.Library.Metadata.GetAsync(item.Id, Token)).Tags, tag => tag.Tag.Value == "holiday");
    }

    [Fact]
    public async Task Rescan_FileMovedToAnotherFolder_IsRelinkedByALibraryRescan()
    {
        var otherRoot = bed.Root + "-other";
        Directory.CreateDirectory(otherRoot);
        var other = IndexedFolder.Create(bed.Library.Library.Id, otherRoot, otherRoot, DateTimeOffset.UtcNow);
        await bed.Library.Folders.AddAsync(other, Token);
        try
        {
            bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
            await ImportAsync();
            var item = await ItemAsync("photo.jpg");

            File.Move(Path.Combine(bed.Root, "photo.jpg"), Path.Combine(otherRoot, "photo.jpg"));
            await ReconcilerTests.RescanAsync(bed, library: true);

            var moved = await bed.Library.Items.GetAsync(item.Id, Token);
            Assert.Equal(other.Id, moved!.FolderId);
            Assert.Equal(1, await bed.Library.Items.CountAsync(Token));
        }
        finally
        {
            Directory.Delete(otherRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Rescan_MissingItemFoundLaterInAnotherFolder_IsRelinked()
    {
        var otherRoot = bed.Root + "-other";
        Directory.CreateDirectory(otherRoot);
        var other = IndexedFolder.Create(bed.Library.Library.Id, otherRoot, otherRoot, DateTimeOffset.UtcNow);
        await bed.Library.Folders.AddAsync(other, Token);
        try
        {
            bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
            await ImportAsync();
            var item = await ItemAsync("photo.jpg");
            File.Move(Path.Combine(bed.Root, "photo.jpg"), Path.Combine(otherRoot, "photo.jpg"));
            await ReconcilerTests.RescanAsync(bed);
            Assert.Equal(MediaItemStatus.Missing, (await bed.Library.Items.GetAsync(item.Id, Token))!.Status);

            await ReconcilerTests.RescanAsync(bed, other);

            var moved = await bed.Library.Items.GetAsync(item.Id, Token);
            Assert.Equal(other.Id, moved!.FolderId);
            Assert.NotEqual(MediaItemStatus.Missing, moved.Status);
            Assert.Equal(1, await bed.Library.Items.CountAsync(Token));
        }
        finally
        {
            Directory.Delete(otherRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Rescan_TwoIdenticalFilesMoved_PairsByFileNameOnly()
    {
        var photo = await File.ReadAllBytesAsync(SampleMedia.JpegWithExif, Token);
        await File.WriteAllBytesAsync(Path.Combine(bed.Root, "one.jpg"), photo, Token);
        await File.WriteAllBytesAsync(Path.Combine(bed.Root, "two.jpg"), photo, Token);
        await ImportAsync();
        var one = await ItemAsync("one.jpg");
        var two = await ItemAsync("two.jpg");

        Directory.CreateDirectory(Path.Combine(bed.Root, "moved"));
        File.Move(Path.Combine(bed.Root, "one.jpg"), Path.Combine(bed.Root, "moved/one.jpg"));
        File.Move(Path.Combine(bed.Root, "two.jpg"), Path.Combine(bed.Root, "moved/renamed.jpg"));
        var result = await bed.Get<Reconciler>().ReconcileAsync(bed.Library, [bed.Folder], Token);

        // Same content, so only the file name tells them apart; the renamed one can't be told from a copy.
        var pair = Assert.Single(result.Moved);
        Assert.Equal(one.Id, pair.Item.Id);
        Assert.Equal("moved/one.jpg", pair.Found.Path.Value);
        Assert.Equal(two.Id, Assert.Single(result.Missing).Id);
        Assert.Equal("moved/renamed.jpg", Assert.Single(result.New).Path.Value);
    }

    [Fact]
    public async Task Reconcile_SameFingerprintButOtherContent_IsNotAMove()
    {
        // Larger than three fingerprint chunks, so a byte between them changes the content but not the fingerprint.
        byte[] photo = [.. await File.ReadAllBytesAsync(SampleMedia.JpegWithExif, Token), .. new byte[400_000]];
        await File.WriteAllBytesAsync(Path.Combine(bed.Root, "a.jpg"), photo, Token);
        await ImportAsync();
        var item = await ItemAsync("a.jpg");
        Assert.NotNull(item.Hashes.ContentHash);

        photo[100_000] ^= 0xFF;
        File.Delete(Path.Combine(bed.Root, "a.jpg"));
        await File.WriteAllBytesAsync(Path.Combine(bed.Root, "b.jpg"), photo, Token);
        var result = await bed.Get<Reconciler>().ReconcileAsync(bed.Library, [bed.Folder], Token);

        Assert.Equal(item.Hashes.Fingerprint, Assert.Single(result.New).Hashes.Fingerprint);
        Assert.Empty(result.Moved);
        Assert.Equal(item.Id, Assert.Single(result.Missing).Id);
    }

    private async Task ImportAsync()
    {
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    private async Task TagAsync(MediaItem item)
    {
        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.AddTag(TagName.From("holiday"), DateTimeOffset.UtcNow), Token);
        item.SetFavorite(true, DateTimeOffset.UtcNow);
        await bed.Library.Items.UpdateAsync(item, Token);
    }

    private async Task<MediaItem> ItemAsync(string path) => (await bed.ItemsAsync()).Single(item => item.Path.Value == path);
}
