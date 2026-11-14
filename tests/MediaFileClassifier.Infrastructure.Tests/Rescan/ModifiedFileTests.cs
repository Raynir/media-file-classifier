using System.Security.Cryptography;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;

namespace MediaFileClassifier.Infrastructure.Tests.Rescan;

/// <summary>
/// Changed files are refreshed and touched ones aren't, with the user's values kept (E02-S04-T03).
/// </summary>
public sealed class ModifiedFileTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync();

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task Rescan_ImageEditedElsewhere_GetsNewThumbnailAndMetadataAndKeepsUserTags()
    {
        var path = bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        var item = await ItemAsync();
        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.AddTag(TagName.From("holiday"), DateTimeOffset.UtcNow), Token);
        var oldThumbnail = await ThumbnailHashAsync(item);
        var oldWidth = (await bed.Library.Items.GetTechnicalMetadataAsync(item.Id, Token))!.Width;
        var registered = bed.Registered.Received.Count;

        // What an editor's "save" does: other pixels and size, written over the same file.
        File.Copy(SampleMedia.JpegWithIccAndOffset, path, overwrite: true);
        await ReconcilerTests.RescanAsync(bed);

        var refreshed = await ItemAsync();
        Assert.Equal(item.Id, refreshed.Id);
        Assert.Equal(MediaItemStatus.Pending, refreshed.Status);
        Assert.Equal(new FileInfo(path).Length, refreshed.SizeBytes);
        Assert.NotEqual(item.Hashes.ContentHash, refreshed.Hashes.ContentHash);
        Assert.NotNull(refreshed.Hashes.ContentHash);
        Assert.NotEqual(oldThumbnail, await ThumbnailHashAsync(refreshed));
        Assert.NotEqual(oldWidth, (await bed.Library.Items.GetTechnicalMetadataAsync(item.Id, Token))!.Width);
        Assert.Contains((await bed.Library.Metadata.GetAsync(item.Id, Token)).Tags, tag => tag.Tag.Value == "holiday");
        var announced = Assert.Single(bed.Registered.Received.Skip(registered));
        Assert.True(announced.ContentChanged);
    }

    [Fact]
    public async Task Rescan_TouchedFile_OnlyGetsTheNewTime()
    {
        var path = bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        var item = await ItemAsync();
        var touched = DateTime.UtcNow.AddMinutes(5);
        File.SetLastWriteTimeUtc(path, touched);
        var registered = bed.Registered.Received.Count;

        await ReconcilerTests.RescanAsync(bed);

        var after = await ItemAsync();
        Assert.Equal(touched.ToUniversalTime(), after.FileModifiedAt.UtcDateTime, TimeSpan.FromMilliseconds(1));
        Assert.Equal(item.Hashes, after.Hashes);
        Assert.Equal(item.Status, after.Status);
        Assert.Equal(registered, bed.Registered.Received.Count);
    }

    [Fact]
    public async Task Rescan_SameSizeEditBetweenFingerprintChunks_IsFoundByTheContentHash()
    {
        byte[] photo = [.. await File.ReadAllBytesAsync(SampleMedia.JpegWithExif, Token), .. new byte[400_000]];
        var path = Path.Combine(bed.Root, "big.jpg");
        await File.WriteAllBytesAsync(path, photo, Token);
        await ImportAsync();
        var item = await ItemAsync();

        photo[100_000] ^= 0xFF;
        await File.WriteAllBytesAsync(path, photo, Token);
        await ReconcilerTests.RescanAsync(bed);

        var after = await ItemAsync();
        Assert.Equal(item.Hashes.Fingerprint, after.Hashes.Fingerprint);
        Assert.NotEqual(item.Hashes.ContentHash, after.Hashes.ContentHash);
        Assert.True(Assert.Single(bed.Registered.Received, notification => notification.ItemId == item.Id && notification.ContentChanged).ContentChanged);
    }

    [Fact]
    public async Task Rescan_MissingFileBackUnchanged_IsRestoredWithoutRefresh()
    {
        var path = bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        var item = await ItemAsync();
        File.Move(path, path + ".away");
        await ReconcilerTests.RescanAsync(bed);
        Assert.Equal(MediaItemStatus.Missing, (await ItemAsync()).Status);
        var registered = bed.Registered.Received.Count;

        File.Move(path + ".away", path);
        await ReconcilerTests.RescanAsync(bed);

        var after = await ItemAsync();
        Assert.Equal(MediaItemStatus.Pending, after.Status);
        Assert.Equal(item.Hashes, after.Hashes);
        Assert.Equal(registered, bed.Registered.Received.Count);
    }

    [Fact]
    public async Task Rescan_DamagedFileReplacedWithAValidOne_IsProcessed()
    {
        var path = bed.Copy(SampleMedia.CorruptJpeg, "photo.jpg");
        await ImportAsync();
        var item = await ItemAsync();
        Assert.True(item.Error!.IsPermanent);

        File.Copy(SampleMedia.JpegWithExif, path, overwrite: true);
        await ReconcilerTests.RescanAsync(bed);

        var after = await ItemAsync();
        Assert.Equal(MediaItemStatus.Pending, after.Status);
        Assert.Null(after.Error);
        Assert.NotNull(await bed.Library.Items.GetTechnicalMetadataAsync(item.Id, Token));
        Assert.NotNull(await ThumbnailHashAsync(after));
    }

    [Fact]
    public async Task Rescan_DamagedFileUnchanged_StaysFailed()
    {
        bed.Copy(SampleMedia.CorruptJpeg, "photo.jpg");
        await ImportAsync();
        var failed = await ItemAsync();

        await ReconcilerTests.RescanAsync(bed);

        var after = await ItemAsync();
        Assert.Equal(MediaItemStatus.Error, after.Status);
        Assert.Equal(failed.Error, after.Error);
    }

    [Fact]
    public async Task Rescan_DamagedFileReplacedWithAnotherDamagedOne_IsTriedAndFailsAgain()
    {
        var path = bed.Copy(SampleMedia.CorruptJpeg, "photo.jpg");
        await ImportAsync();
        var size = (await ItemAsync()).SizeBytes;

        File.AppendAllBytes(path, [0, 1, 2, 3, 4, 5, 6, 7]);
        await ReconcilerTests.RescanAsync(bed);

        var after = await ItemAsync();
        Assert.Equal(MediaItemStatus.Error, after.Status);
        Assert.Equal(size + 8, after.SizeBytes);
        Assert.True(after.Error!.IsPermanent);
    }

    [Fact]
    public async Task Rescan_FixedFile_IsNoLongerListedAsFailed()
    {
        var path = bed.Copy(SampleMedia.CorruptJpeg, "photo.jpg");
        await ImportAsync();
        Assert.Single(await bed.Library.Items.GetByStatusAsync(MediaItemStatus.Error, null, Token));

        File.Copy(SampleMedia.JpegWithExif, path, overwrite: true);
        await ReconcilerTests.RescanAsync(bed);

        Assert.Empty(await bed.Library.Items.GetByStatusAsync(MediaItemStatus.Error, null, Token));
    }

    private async Task ImportAsync()
    {
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    private async Task<MediaItem> ItemAsync() => Assert.Single(await bed.ItemsAsync());

    private async Task<string?> ThumbnailHashAsync(MediaItem item)
    {
        var file = bed.Get<IThumbnailCache>().Find(bed.Library.Layout, item.Id, ThumbnailSize.Small, item.FileModifiedAt);
        return file is null ? null : Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file, Token)));
    }
}
