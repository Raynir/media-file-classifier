using System.Security.Cryptography;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Tests.Jobs;
using MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.RetryFailedItems;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// End-to-end tests of the import pipeline on real files, a real library and the real job system (E03-S07).
/// </summary>
public sealed class ImportPipelineTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync();

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task Import_SampleCorpus_EveryFileEndsInADefinedState()
    {
        CopyDirectory(SampleMedia.Directory, bed.Root);

        var jobId = await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();

        var items = (await bed.ItemsAsync()).ToDictionary(item => item.Path.Value, StringComparer.Ordinal);

        // Not media: skipped and counted by discovery, never registered (the next scan looks at them again).
        Assert.DoesNotContain("zero-byte.png", items.Keys);
        Assert.DoesNotContain("audio-only.mp4", items.Keys);

        var corrupt = items["corrupt.jpg"];
        Assert.Equal(MediaItemStatus.Error, corrupt.Status);
        Assert.Equal(new MediaItemError(ImportStages.Media, null, IsPermanent: true), corrupt.Error);
        Assert.NotNull(corrupt.StatusReason);

        var imported = items.Values.Where(item => item != corrupt).ToList();
        Assert.Equal(CountMediaFiles() - 3, imported.Count);
        foreach (var item in imported)
        {
            Assert.True(item.Status == MediaItemStatus.Pending, $"{item.Path} is {item.Status}: {item.StatusReason}");
            Assert.NotNull(item.Hashes.Fingerprint);
            Assert.NotNull(item.Hashes.ContentHash);
            var metadata = await bed.Library.Items.GetTechnicalMetadataAsync(item.Id, Token);
            Assert.NotNull(metadata);
            if (item.MediaType != MediaType.Video || ThumbnailTestSupport.HasFfmpeg)
            {
                Assert.True(File.Exists(bed.Library.Layout.GetThumbnailPath(item.Id, ThumbnailSize.Small)), $"{item.Path} has no thumbnail");
            }
            else
            {
                Assert.Contains(metadata.Warnings, warning => warning.Code == ThumbnailWarnings.ThumbnailFfmpegUnavailable);
            }
        }

        Assert.Equal(imported.Select(item => item.Id.ToString()).Order(), bed.Registered.Received.Select(registered => registered.ItemId.ToString()).Order());
        Assert.Equal(ProcessingJobState.Completed, (await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State);

        // Exact copies, e.g. the robustness set's moved and renamed originals, are grouped by content hash.
        var expected = imported
            .GroupBy(item => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(bed.Root, item.Path.Value)))))
            .Where(group => group.Count() > 1)
            .Select(group => string.Join('|', group.Select(item => item.Path.Value).Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal);
        var byId = imported.ToDictionary(item => item.Id);
        var groups = (await bed.Library.Items.GetExactDuplicatesAsync(Token))
            .Select(group => string.Join('|', group.ItemIds.Select(id => byId[id].Path.Value).Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal);
        Assert.Contains("robustness/moved/deeper/renamed-photo-a.jpg|robustness/photo-a.jpg", groups);
        Assert.Equal(expected, groups);
    }

    [Fact]
    public async Task Import_Again_CreatesNoDuplicateItems()
    {
        bed.WriteDistinctPhotos(20);
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var first = await bed.ItemsAsync();

        var again = await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();

        var second = await bed.ItemsAsync();
        Assert.Equal(first.Select(item => $"{item.Id} {item.ImportedAt:O}").Order(), second.Select(item => $"{item.Id} {item.ImportedAt:O}").Order());
        Assert.All(second, item => Assert.Equal(MediaItemStatus.Pending, item.Status));
        Assert.Equal([ImportStages.Discover], (await bed.Library.Jobs.GetWorkItemsAsync(again, Token)).Select(workItem => workItem.Stage));
        Assert.Equal(20, bed.Registered.Received.Count);
    }

    [Fact]
    public async Task Import_WhileOneIsUnfinished_IsNotQueuedTwice()
    {
        bed.WriteDistinctPhotos(5);
        await bed.ImportAsync();

        // Reopened with processing paused, so the first import is certainly still unfinished.
        bed.Store.SetValue(bed.Processing.ResumeOnOpen, ResumeMode.Ask);
        await bed.RestartAsync();
        var started = await bed.Sender.Send(new UseCases.Import.ImportFolders.ImportFoldersCommand([bed.Folder.Id]), Token);

        Assert.True(bed.Jobs.Current!.IsPaused);
        Assert.Empty(started);
        bed.Jobs.Current.Resume();
        await bed.WaitUntilIdleAsync();
        Assert.Equal(5, (await bed.ItemsAsync()).Count);
    }

    [Fact]
    public async Task Import_InterruptedByRestart_ResumesWithoutDuplicates()
    {
        await RecreateAsync(cpu: 1);
        bed.WriteDistinctPhotos(150);
        var jobId = await bed.ImportAsync();
        await WaitUntilAsync(async () => (await bed.ItemsAsync()).Any(item => item.Status == MediaItemStatus.Pending));

        // As if the app was closed mid-import: running work is stopped and goes back to the queue.
        await bed.RestartAsync();
        await bed.WaitUntilIdleAsync();

        var items = await bed.ItemsAsync();
        Assert.Equal(150, items.Count);
        Assert.Equal(150, items.Select(item => item.Path.Value).Distinct().Count());
        Assert.All(items, item => Assert.Equal(MediaItemStatus.Pending, item.Status));
        Assert.Equal(ProcessingJobState.Completed, (await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State);
    }

    [Fact]
    public async Task Import_Cancelled_KeepsProcessedItemsAndTheNextImportFinishesTheRest()
    {
        await RecreateAsync(cpu: 1);
        bed.WriteDistinctPhotos(150);
        var jobId = await bed.ImportAsync();
        await WaitUntilAsync(async () => (await bed.ItemsAsync()).Count(item => item.Status == MediaItemStatus.Pending) >= 3);

        Assert.True(await bed.Jobs.Current!.CancelJobAsync(jobId, Token));
        await bed.WaitUntilIdleAsync();

        var afterCancel = await bed.ItemsAsync();
        Assert.Equal(ProcessingJobState.Cancelled, (await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State);
        Assert.DoesNotContain(afterCancel, item => item.Status == MediaItemStatus.Error);
        Assert.Contains(afterCancel, item => item.Status == MediaItemStatus.Discovered);
        var processed = afterCancel.Where(item => item.Status == MediaItemStatus.Pending).Select(item => item.Id).ToHashSet();
        Assert.NotEmpty(processed);

        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();

        var items = await bed.ItemsAsync();
        Assert.Equal(afterCancel.Count, items.Count);
        Assert.All(items, item => Assert.Equal(MediaItemStatus.Pending, item.Status));
    }

    [Fact]
    public async Task Import_LockedFile_FailsAsTransientAndRetrySucceedsOnceUnlocked()
    {
        var photo = bed.Copy(SampleMedia.JpegWithExif, "locked.jpg");
        await bed.Get<IFolderDiscovery>().DiscoverAsync(bed.Library, bed.Folder, cancellationToken: Token);
        Id<ProcessingJob> jobId;
        using (new FileStream(photo, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            jobId = await bed.ImportAsync();
            await bed.WaitUntilIdleAsync();
        }

        var failed = Assert.Single(await bed.ItemsAsync());
        Assert.Equal(MediaItemStatus.Error, failed.Status);
        Assert.Equal(new MediaItemError(ImportStages.Fingerprint, nameof(IOException), IsPermanent: false), failed.Error);
        var workItem = (await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token)).Single(item => item.Stage == ImportStages.Fingerprint);
        Assert.Equal(WorkItemState.Failed, workItem.State);
        Assert.StartsWith(failed.StatusReason!, workItem.LastError, StringComparison.Ordinal);

        var result = await bed.Sender.Send(new RetryFailedItemsCommand(jobId), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(new RetryFailedItemsResult(1, 0), result);
        var item = Assert.Single(await bed.ItemsAsync());
        Assert.Equal(MediaItemStatus.Pending, item.Status);
        Assert.Null(item.Error);
    }

    [Fact]
    public async Task Retry_SelectedItems_RetriesOnlyThose()
    {
        var first = bed.Copy(SampleMedia.JpegWithExif, "first.jpg");
        var second = bed.Copy(SampleMedia.JpegWithExif, "second.jpg");
        await bed.Get<IFolderDiscovery>().DiscoverAsync(bed.Library, bed.Folder, cancellationToken: Token);
        using (new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await bed.ImportAsync();
            await bed.WaitUntilIdleAsync();
        }

        var items = await bed.ItemsAsync();
        Assert.All(items, item => Assert.Equal(MediaItemStatus.Error, item.Status));
        var chosen = items.Single(item => item.FileName == "first.jpg");

        var result = await bed.Sender.Send(new RetryFailedItemsCommand(ItemIds: [chosen.Id]), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(new RetryFailedItemsResult(1, 0), result);
        var after = await bed.ItemsAsync();
        Assert.Equal(MediaItemStatus.Pending, after.Single(item => item.Id == chosen.Id).Status);
        Assert.Equal(MediaItemStatus.Error, after.Single(item => item.Id != chosen.Id).Status);
    }

    [Fact]
    public async Task Retry_DamagedFile_WaitsUntilTheFileChanges()
    {
        var path = bed.Copy(SampleMedia.CorruptJpeg, "damaged.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();

        var unchanged = await bed.Sender.Send(new RetryFailedItemsCommand(), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(new RetryFailedItemsResult(0, 1), unchanged);
        Assert.Equal(MediaItemStatus.Error, Assert.Single(await bed.ItemsAsync()).Status);

        // The user repaired the file.
        File.Copy(SampleMedia.JpegWithExif, path, overwrite: true);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        var changed = await bed.Sender.Send(new RetryFailedItemsCommand(), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(new RetryFailedItemsResult(1, 0), changed);
        var item = Assert.Single(await bed.ItemsAsync());
        Assert.Equal(MediaItemStatus.Pending, item.Status);
        Assert.Equal(new FileInfo(path).Length, item.SizeBytes);
        Assert.NotNull(await bed.Library.Items.GetTechnicalMetadataAsync(item.Id, Token));
    }

    [Fact]
    public async Task Import_MissingFolder_MarksTheFolderOfflineAndQueuesNothing()
    {
        Directory.Delete(bed.Root, recursive: true);

        var started = await bed.Sender.Send(new UseCases.Import.ImportFolders.ImportFoldersCommand([bed.Folder.Id]), Token);

        Assert.Empty(started);
        Assert.Equal(Domain.Libraries.FolderStatus.Offline, (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.Status);
    }

    [Fact]
    public async Task ImportWork_ForAFolderGoneBeforeItRuns_IsParked_AndFinishesWhenTheFolderIsBack()
    {
        // MFC-1004: queued import work whose folder was unplugged meanwhile waits for it, without failing the item.
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await bed.Get<IFolderDiscovery>().DiscoverAsync(bed.Library, bed.Folder, cancellationToken: Token);
        var item = Assert.Single(await bed.ItemsAsync());
        var away = bed.Root + "-away";
        Directory.Move(bed.Root, away);

        var jobId = await bed.Jobs.Current!.EnqueueJobAsync(JobSchedulerTests.Job(JobPriority.Import, [new WorkItemRequest(ImportStages.Fingerprint, item.Id)]), Token);
        await WaitUntilAsync(async () => Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token)).IsParked);

        var parked = Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token));
        Assert.Equal(0, parked.RetryCount);
        Assert.Equal(MediaItemStatus.Discovered, Assert.Single(await bed.ItemsAsync()).Status);
        Assert.Equal(Domain.Libraries.FolderStatus.Offline, (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.Status);

        Directory.Move(away, bed.Root);
        await bed.Sender.Send(new CheckFolderAvailabilityCommand(), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(MediaItemStatus.Pending, Assert.Single(await bed.ItemsAsync()).Status);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(5, Token);
        }
    }

    private int CountMediaFiles() =>
        Directory.EnumerateFiles(bed.Root, "*", SearchOption.AllDirectories)
            .Count(file => Path.GetExtension(file).ToUpperInvariant() is ".JPG" or ".PNG" or ".WEBP" or ".GIF" or ".MP4");

    private async Task RecreateAsync(int cpu)
    {
        await bed.DisposeAsync();
        bed = await ImportTestBed.CreateAsync(cpu);
    }
}
