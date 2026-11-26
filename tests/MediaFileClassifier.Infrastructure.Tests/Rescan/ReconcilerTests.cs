using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Testing.Platform;
using MediaFileClassifier.UseCases.Rescan;

namespace MediaFileClassifier.Infrastructure.Tests.Rescan;

/// <summary>
/// The reconciliation engine on the sample corpus, with a real library and real files (E02-S04-T01).
/// </summary>
public sealed class ReconcilerTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync();
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "b/alpha.png");
        bed.Copy(SampleMedia.WebP, "webp.webp");
        bed.Copy(SampleMedia.StaticGif, "gone.gif");
        bed.Copy(SampleMedia.NonAsciiName, "фото с пробелом.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task Reconcile_SampleCorpus_ClassifiesNewUnchangedModifiedAndMissing()
    {
        File.AppendAllBytes(Path.Combine(bed.Root, "webp.webp"), [1, 2, 3]);
        File.SetLastWriteTimeUtc(Path.Combine(bed.Root, "b/alpha.png"), DateTime.UtcNow.AddMinutes(1));
        File.Delete(Path.Combine(bed.Root, "gone.gif"));
        bed.Copy(SampleMedia.AnimatedGif, "new/anim.gif");
        bed.Copy(SampleMedia.ZeroByteFile, "new/empty.png");

        var result = await bed.Get<Reconciler>().ReconcileAsync(bed.Library, [bed.Folder], Token);

        Assert.Equal(["new/anim.gif"], result.New.Select(item => item.Path.Value));
        Assert.Equal(["a/photo.jpg", "фото с пробелом.jpg"], result.Unchanged.Select(item => item.Path.Value).Order(StringComparer.Ordinal));
        Assert.Equal(["b/alpha.png", "webp.webp"], result.Modified.Select(file => file.Item.Path.Value).Order(StringComparer.Ordinal));
        Assert.Equal(["gone.gif"], result.Missing.Select(item => item.Path.Value));
        Assert.Empty(result.Moved);
        Assert.Empty(result.Offline);
        Assert.Equal(bed.Folder.Id, Assert.Single(result.Scanned).Folder.Id);

        // Nothing is written until the result is applied.
        Assert.Equal(5, (await bed.ItemsAsync()).Count);
        Assert.DoesNotContain(await bed.ItemsAsync(), item => item.Status == MediaItemStatus.Missing);
    }

    [Fact]
    public async Task Reconcile_FolderUnpluggedDuringTheListing_IsOffline_AndNothingIsMissing()
    {
        // The listing missed a file, and the folder can't be reached afterwards: its drive was unplugged meanwhile.
        File.Delete(Path.Combine(bed.Root, "gone.gif"));
        var unplugged = new InMemoryMediaFileSource { IsAvailable = false };
        var reconciler = new Reconciler(bed.Get<IFolderDiscovery>(), bed.Get<MoveDetector>(), unplugged);

        var result = await reconciler.ReconcileAsync(bed.Library, [bed.Folder], Token);

        Assert.Equal(bed.Folder.Id, Assert.Single(result.Offline).Id);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Scanned);
    }

    [Fact]
    public async Task Reconcile_FolderWhoseLetterLeadsToAnotherDrive_IsOffline_AndNothingIsListed()
    {
        File.Delete(Path.Combine(bed.Root, "gone.gif"));
        bed.Folder.ChangeVolumeId("1111-1111");
        var volumes = new FakeVolumeLocator();
        volumes.Volumes[Path.GetPathRoot(bed.Root)!] = "2222-2222";
        var reconciler = new Reconciler(bed.Get<IFolderDiscovery>(), bed.Get<MoveDetector>(), new InMemoryMediaFileSource(), volumes);

        var result = await reconciler.ReconcileAsync(bed.Library, [bed.Folder], Token);

        Assert.Equal(bed.Folder.Id, Assert.Single(result.Offline).Id);
        Assert.Empty(result.Missing);
        Assert.Empty(result.New);
    }

    [Fact]
    public async Task Reconcile_MissingFileBackAtItsPath_IsModified()
    {
        var gone = Path.Combine(bed.Root, "gone.gif");
        var bytes = await File.ReadAllBytesAsync(gone, Token);
        File.Delete(gone);
        await RescanAsync(bed);
        Assert.Equal(MediaItemStatus.Missing, (await ItemAsync("gone.gif")).Status);

        await File.WriteAllBytesAsync(gone, bytes, Token);
        var result = await bed.Get<Reconciler>().ReconcileAsync(bed.Library, [bed.Folder], Token);

        Assert.Equal("gone.gif", Assert.Single(result.Modified).Item.Path.Value);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public async Task Reconcile_ExcludedItem_IsLeftOut()
    {
        var excluded = await ItemAsync("webp.webp");
        excluded.ChangeStatus(MediaItemStatus.Excluded, DateTimeOffset.UtcNow);
        await bed.Library.Items.UpdateAsync(excluded, Token);
        File.Delete(Path.Combine(bed.Root, "webp.webp"));

        var result = await bed.Get<Reconciler>().ReconcileAsync(bed.Library, [bed.Folder], Token);

        Assert.Empty(result.Missing);
        Assert.DoesNotContain(result.Unchanged, item => item.Id == excluded.Id);
    }

    [Fact]
    public async Task Reconcile_OfflineFolder_IsSkippedAndItsItemsStay()
    {
        var root = bed.Root + "-away";
        Directory.Move(bed.Root, root);
        try
        {
            var result = await bed.Get<Reconciler>().ReconcileAsync(bed.Library, [bed.Folder], Token);

            Assert.Equal(bed.Folder.Id, Assert.Single(result.Offline).Id);
            Assert.Empty(result.Missing);
            Assert.Empty(result.Scanned);
        }
        finally
        {
            Directory.Move(root, bed.Root);
        }
    }

    [Fact]
    public async Task Rescan_AppliesNewAndMissing_AndMarksOfflineFolders()
    {
        File.Delete(Path.Combine(bed.Root, "gone.gif"));
        bed.Copy(SampleMedia.AnimatedGif, "new/anim.gif");

        await RescanAsync(bed);

        var items = await bed.ItemsAsync();
        Assert.Equal(MediaItemStatus.Missing, items.Single(item => item.Path.Value == "gone.gif").Status);
        Assert.Equal(MediaItemStatus.Pending, items.Single(item => item.Path.Value == "new/anim.gif").Status);
        Assert.Equal(6, items.Count);

        var root = bed.Root + "-away";
        Directory.Move(bed.Root, root);
        try
        {
            await RescanAsync(bed);
            Assert.Equal(FolderStatus.Offline, (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.Status);
            Assert.Equal(1, (await bed.ItemsAsync()).Count(item => item.Status == MediaItemStatus.Missing));
        }
        finally
        {
            Directory.Move(root, bed.Root);
        }

        await RescanAsync(bed);
        Assert.Equal(FolderStatus.Online, (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.Status);
    }

    /// <summary>
    /// Queues a rescan job, as the rescan commands do (E02-S04-T06), and waits for it and the work it queues.
    /// </summary>
    /// <param name="bed">The test bed.</param>
    /// <param name="folder">The folder to rescan, or null for the whole library.</param>
    /// <param name="library">Whether to rescan every folder of the library instead.</param>
    /// <returns>The job.</returns>
    internal static async Task<Id<ProcessingJob>> RescanAsync(ImportTestBed bed, IndexedFolder? folder = null, bool library = false)
    {
        var jobId = await bed.Jobs.Current!.EnqueueJobAsync(
            new JobRequest(RescanStages.RescanJob, "Rescan", JobPriority.UserAction, [new WorkItemRequest(RescanStages.Reconcile)])
            {
                FolderId = library ? null : (folder ?? bed.Folder).Id,
            },
            Token);
        await bed.WaitUntilIdleAsync();
        return jobId;
    }

    private async Task<MediaItem> ItemAsync(string path) => (await bed.ItemsAsync()).Single(item => item.Path.Value == path);
}
