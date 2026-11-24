using System.Diagnostics;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;
using MediaFileClassifier.UseCases.Rescan;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Monitoring;

/// <summary>
/// Watched changes reach the library through the job system (E04-S02-T01): each kind of change, on a real folder and
/// library. Deletions are held for the grace period (E04-S02-T02) on a manual clock.
/// </summary>
public sealed class ChangeProcessingFlowTests : IAsyncLifetime
{
    private readonly ManualClock clock = new(DateTimeOffset.UtcNow);
    private readonly MonitoringPause monitoringPause = new();
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync(configure: services =>
        services.AddSingleton(provider => new ChangeProcessor(provider.GetRequiredService<HeldDeletes>(), clock, NullLogger<ChangeProcessor>.Instance, provider.GetRequiredService<IJobSystem>(), monitoringPause)));

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task Changed_NewFile_IsImportedInAnImportJobOfTheFolder()
    {
        await ImportAsync();
        bed.Copy(SampleMedia.JpegWithExif, "new/photo.jpg");

        await ApplyAsync(await ChangedAsync("new/photo.jpg"));

        var item = await ItemAsync("new/photo.jpg");
        Assert.Equal(MediaItemStatus.Pending, item.Status);
        Assert.NotNull(item.Hashes.Fingerprint);
        var job = (await bed.Library.Jobs.GetRecentJobsAsync(20, Token)).Single(other => other.Description.StartsWith("Import changes", StringComparison.Ordinal));
        Assert.Equal((ImportStages.ImportJob, bed.Folder.Id, (int)JobPriority.Import), (job.Type, job.FolderId, job.Priority));
    }

    [Fact]
    public async Task Changed_EditedFile_IsRefreshedAndQueuedForAnalysis_KeepingUserMetadata()
    {
        var path = bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        var item = await ItemAsync("photo.jpg");
        await TagAsync(item);
        var registered = bed.Registered.Received.Count;

        File.Copy(SampleMedia.JpegWithIccAndOffset, path, overwrite: true);
        await ApplyAsync(await ChangedAsync("photo.jpg"));

        var refreshed = await ItemAsync("photo.jpg");
        Assert.Equal(item.Id, refreshed.Id);
        Assert.Equal(new FileInfo(path).Length, refreshed.SizeBytes);
        Assert.Contains((await bed.Library.Metadata.GetAsync(item.Id, Token)).Tags, tag => tag.Tag.Value == "holiday");
        Assert.True(Assert.Single(bed.Registered.Received.Skip(registered)).ContentChanged);
    }

    [Fact]
    public async Task Changed_UnchangedFile_IsLeftAlone()
    {
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        var item = await ItemAsync("photo.jpg");
        var registered = bed.Registered.Received.Count;

        await ApplyAsync(await ChangedAsync("photo.jpg"));

        Assert.Equal(item.Status, (await ItemAsync("photo.jpg")).Status);
        Assert.Equal(registered, bed.Registered.Received.Count);
    }

    [Fact]
    public async Task Changed_MissingItemsFileIsBack_IsNoLongerMissing()
    {
        var path = bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        var bytes = await File.ReadAllBytesAsync(path, Token);
        File.Delete(path);
        await DeleteAsync("photo.jpg");
        Assert.Equal(MediaItemStatus.Missing, (await ItemAsync("photo.jpg")).Status);

        await File.WriteAllBytesAsync(path, bytes, Token);
        await ApplyAsync(await ChangedAsync("photo.jpg"));

        Assert.NotEqual(MediaItemStatus.Missing, (await ItemAsync("photo.jpg")).Status);
    }

    [Theory]
    [InlineData("renamed.jpg")]
    [InlineData("sub/deeper/photo.jpg")]
    public async Task Renamed_File_RelinksItsItem_KeepingMetadataWithoutReanalysis(string target)
    {
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        var item = await ItemAsync("photo.jpg");
        await TagAsync(item);
        var registered = bed.Registered.Received.Count;

        Move("photo.jpg", target);
        await ApplyAsync(await RenamedAsync("photo.jpg", target));

        var items = await bed.ItemsAsync();
        var moved = Assert.Single(items);
        Assert.Equal((item.Id, target, item.Status), (moved.Id, moved.Path.Value, moved.Status));
        Assert.True(moved.IsFavorite);
        Assert.Contains((await bed.Library.Metadata.GetAsync(item.Id, Token)).Tags, tag => tag.Tag.Value == "holiday");
        Assert.Equal(registered, bed.Registered.Received.Count);
    }

    [Fact]
    public async Task Renamed_FolderOf5000Items_RelinksThemAllWithoutReanalysis()
    {
        const int Count = 5000;
        var items = await AddItemsAsync("album", Count);
        var registered = bed.Registered.Received.Count;

        Directory.Move(Path.Combine(bed.Root, "album"), Path.Combine(bed.Root, "renamed"));
        FolderChange[] changes = [.. items.Select(item =>
        {
            var path = "renamed/" + item.Path.FileName;
            var file = new FileInfo(Path.Combine(bed.Root, path));
            return new FolderChange(FolderChangeKind.Renamed, path, item.Path.Value, new MediaFileStat(path, file.Length, file.LastWriteTimeUtc));
        })];
        var stopwatch = Stopwatch.StartNew();
        await ApplyAsync(changes);
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"Relinked {Count} items in {stopwatch.ElapsedMilliseconds} ms");

        var relinked = (await bed.ItemsAsync()).ToDictionary(item => item.Id);
        Assert.Equal(Count, relinked.Count);
        Assert.All(items, item =>
        {
            var moved = relinked[item.Id];
            Assert.Equal(("renamed/" + item.Path.FileName, MediaItemStatus.Indexed), (moved.Path.Value, moved.Status));
        });
        Assert.Equal(registered, bed.Registered.Received.Count);
        var job = (await bed.Library.Jobs.GetRecentJobsAsync(20, Token)).Single(other => other.Description.StartsWith("Import changes", StringComparison.Ordinal));
        Assert.All(await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token), work => Assert.Equal(ChangeProcessingStages.Apply, work.Stage));
    }

    [Fact]
    public async Task Renamed_TwoFilesSwappedInOneBatch_RelinksBoth()
    {
        bed.Copy(SampleMedia.JpegWithExif, "a.jpg");
        bed.Copy(SampleMedia.JpegWithIccAndOffset, "b.jpg");
        await ImportAsync();
        var a = await ItemAsync("a.jpg");
        var b = await ItemAsync("b.jpg");

        Move("a.jpg", "tmp.jpg");
        Move("b.jpg", "a.jpg");
        Move("tmp.jpg", "b.jpg");
        await ApplyAsync(await RenamedAsync("a.jpg", "tmp.jpg"), await RenamedAsync("b.jpg", "a.jpg"), await RenamedAsync("tmp.jpg", "b.jpg"));

        Assert.Equal(b.Id, (await ItemAsync("a.jpg")).Id);
        Assert.Equal(a.Id, (await ItemAsync("b.jpg")).Id);
    }

    [Fact]
    public async Task Renamed_ExcludedItem_MovesAndStaysExcluded()
    {
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        var item = await ExcludeAsync("photo.jpg");
        var registered = bed.Registered.Received.Count;

        Move("photo.jpg", "sub/photo.jpg");
        await ApplyAsync(await RenamedAsync("photo.jpg", "sub/photo.jpg"));

        var moved = Assert.Single(await bed.ItemsAsync());
        Assert.Equal((item.Id, "sub/photo.jpg", MediaItemStatus.Excluded), (moved.Id, moved.Path.Value, moved.Status));
        Assert.Equal(registered, bed.Registered.Received.Count);
    }

    [Fact]
    public async Task Changed_PendingItemWhoseFileIsTheSame_IsChecked()
    {
        // A batch stopped after the item left Missing may not have queued its check.
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        Assert.Equal(MediaItemStatus.Pending, (await ItemAsync("photo.jpg")).Status);

        await ApplyAsync(await ChangedAsync("photo.jpg"));

        var job = Assert.Single(await ChangeJobsAsync());
        Assert.Contains(await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token), work => work.Stage == RescanStages.Check);
    }

    [Fact]
    public async Task Renamed_FileTheLibraryDoesNotHave_IsImportedAtItsNewPath()
    {
        await ImportAsync();
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");

        await ApplyAsync(await RenamedAsync("unknown.jpg", "photo.jpg"));

        Assert.Equal(MediaItemStatus.Pending, (await ItemAsync("photo.jpg")).Status);
    }

    [Fact]
    public async Task Renamed_OverAnotherItemsFile_ChecksThatItemAndMarksTheMovedOneMissing()
    {
        bed.Copy(SampleMedia.JpegWithExif, "a.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "b.png");
        await ImportAsync();
        var a = await ItemAsync("a.jpg");
        var b = await ItemAsync("b.png");

        File.Move(Path.Combine(bed.Root, "a.jpg"), Path.Combine(bed.Root, "b.png"), overwrite: true);
        await ApplyAsync(await RenamedAsync("a.jpg", "b.png"));

        Assert.Equal(MediaItemStatus.Missing, (await bed.Library.Items.GetAsync(a.Id, Token))!.Status);
        var replaced = await bed.Library.Items.GetAsync(b.Id, Token);
        Assert.Equal(new FileInfo(Path.Combine(bed.Root, "b.png")).Length, replaced!.SizeBytes);
    }

    [Fact]
    public async Task Deleted_File_MarksItsItemMissing_KeepingMetadata()
    {
        var path = bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        var item = await ItemAsync("photo.jpg");
        await TagAsync(item);

        File.Delete(path);
        await DeleteAsync("photo.jpg");

        Assert.Equal(MediaItemStatus.Missing, (await ItemAsync("photo.jpg")).Status);
        Assert.Contains((await bed.Library.Metadata.GetAsync(item.Id, Token)).Tags, tag => tag.Tag.Value == "holiday");
    }

    [Fact]
    public async Task Deleted_Folder_MarksEveryItemUnderItMissing()
    {
        bed.Copy(SampleMedia.JpegWithExif, "trip/day1/a.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "trip/b.png");
        bed.Copy(SampleMedia.JpegWithExif, "kept.jpg");
        await ImportAsync();

        Directory.Delete(Path.Combine(bed.Root, "trip"), recursive: true);
        await DeleteAsync("trip");

        var items = await bed.ItemsAsync();
        Assert.Equal(MediaItemStatus.Missing, items.Single(item => item.Path.Value == "trip/day1/a.jpg").Status);
        Assert.Equal(MediaItemStatus.Missing, items.Single(item => item.Path.Value == "trip/b.png").Status);
        Assert.NotEqual(MediaItemStatus.Missing, items.Single(item => item.Path.Value == "kept.jpg").Status);
    }

    [Fact]
    public async Task Deleted_FileThatIsThereAgain_IsNotMarked()
    {
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();

        await DeleteAsync("photo.jpg");

        Assert.NotEqual(MediaItemStatus.Missing, (await ItemAsync("photo.jpg")).Status);
    }

    [Fact]
    public async Task Deleted_WhileTheFolderCannotBeReached_MarksNothing()
    {
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();
        var away = bed.Root + "-away";
        Directory.Move(bed.Root, away);
        try
        {
            await DeleteAsync("photo.jpg");

            Assert.NotEqual(MediaItemStatus.Missing, (await ItemAsync("photo.jpg")).Status);
        }
        finally
        {
            Directory.Move(away, bed.Root);
        }
    }

    [Fact]
    public async Task Deleted_File_IsHeldForTheGracePeriod()
    {
        var path = bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await ImportAsync();

        File.Delete(path);
        await ApplyAsync(new FolderChange(FolderChangeKind.Deleted, "photo.jpg"));

        Assert.NotEqual(MediaItemStatus.Missing, (await ItemAsync("photo.jpg")).Status);
        Assert.Equal([(bed.Folder.Id, "photo.jpg")], bed.Get<HeldDeletes>().GetAll());
        await ReleaseHeldAsync();
        Assert.Equal(MediaItemStatus.Missing, (await ItemAsync("photo.jpg")).Status);
        Assert.Empty(bed.Get<HeldDeletes>().GetAll());
    }

    [Theory]
    [InlineData("b/photo.jpg")]
    [InlineData("b/renamed.jpg")]
    public async Task MoveBetweenSubfolders_DeleteThenCreate_KeepsTheItemAndItsMetadata(string target)
    {
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "a/other.png");
        await ImportAsync();
        var item = await ItemAsync("a/photo.jpg");
        await TagAsync(item);

        Move("a/photo.jpg", target);
        await ApplyAsync(new FolderChange(FolderChangeKind.Deleted, "a/photo.jpg"));
        await ApplyAsync(await ChangedAsync(target));
        await ReleaseHeldAsync();

        var items = await bed.ItemsAsync();
        Assert.Equal(2, items.Count);
        var moved = Assert.Single(items, other => other.Id == item.Id);
        Assert.Equal(target, moved.Path.Value);
        Assert.NotEqual(MediaItemStatus.Missing, moved.Status);
        Assert.True(moved.IsFavorite);
        Assert.Contains((await bed.Library.Metadata.GetAsync(item.Id, Token)).Tags, tag => tag.Tag.Value == "holiday");
    }

    [Fact]
    public async Task Move_CreatedAfterTheGracePeriod_RelinksTheMissingItem()
    {
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        await ImportAsync();
        var item = await ItemAsync("a/photo.jpg");
        await TagAsync(item);

        Move("a/photo.jpg", "b/photo.jpg");
        await DeleteAsync("a/photo.jpg");
        Assert.Equal(MediaItemStatus.Missing, (await bed.Library.Items.GetAsync(item.Id, Token))!.Status);
        await ApplyAsync(await ChangedAsync("b/photo.jpg"));

        var moved = Assert.Single(await bed.ItemsAsync());
        Assert.Equal((item.Id, "b/photo.jpg"), (moved.Id, moved.Path.Value));
        Assert.NotEqual(MediaItemStatus.Missing, moved.Status);
        Assert.Contains((await bed.Library.Metadata.GetAsync(item.Id, Token)).Tags, tag => tag.Tag.Value == "holiday");
    }

    [Fact]
    public async Task Copy_OfAFileThatIsStillThere_IsImportedAsANewItem()
    {
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        await ImportAsync();
        var item = await ItemAsync("a/photo.jpg");

        bed.Copy(SampleMedia.JpegWithExif, "b/photo.jpg");
        await ApplyAsync(await ChangedAsync("b/photo.jpg"));

        var items = await bed.ItemsAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal("a/photo.jpg", items.Single(other => other.Id == item.Id).Path.Value);
    }

    [Fact]
    public async Task MoveBetweenSubfolders_OfAnExcludedItem_KeepsItExcluded()
    {
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        await ImportAsync();
        var item = await ExcludeAsync("a/photo.jpg");

        Move("a/photo.jpg", "b/photo.jpg");
        await ApplyAsync(new FolderChange(FolderChangeKind.Deleted, "a/photo.jpg"));
        await ApplyAsync(await ChangedAsync("b/photo.jpg"));
        await ReleaseHeldAsync();

        var moved = Assert.Single(await bed.ItemsAsync());
        Assert.Equal((item.Id, "b/photo.jpg", MediaItemStatus.Excluded), (moved.Id, moved.Path.Value, moved.Status));
    }

    [Fact]
    public async Task DeleteThenCreate_WithOtherContent_IsNotAMove()
    {
        var path = bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        await ImportAsync();
        var item = await ItemAsync("a/photo.jpg");

        File.Delete(path);
        bed.Copy(SampleMedia.JpegWithIccAndOffset, "b/photo.jpg");
        await ApplyAsync(new FolderChange(FolderChangeKind.Deleted, "a/photo.jpg"));
        await ApplyAsync(await ChangedAsync("b/photo.jpg"));
        await ReleaseHeldAsync();

        Assert.Equal(MediaItemStatus.Missing, (await bed.Library.Items.GetAsync(item.Id, Token))!.Status);
        Assert.NotEqual(item.Id, (await ItemAsync("b/photo.jpg")).Id);
    }

    [Fact]
    public async Task Changes_WhileProcessingIsPaused_WaitForResume()
    {
        await ImportAsync();
        var scheduler = bed.Jobs.Current!;
        scheduler.Pause();
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");

        await bed.Get<ChangeProcessor>().SubmitAsync(bed.Folder.Id, [await ChangedAsync("photo.jpg")], Token);
        await AdvanceUntilQueuedAsync(ChangeProcessor.GatherQuiet);
        await Task.Delay(500, Token);
        Assert.Empty(await bed.ItemsAsync());

        scheduler.Resume();
        await bed.WaitUntilIdleAsync();
        Assert.Single(await bed.ItemsAsync());
    }

    [Fact]
    public async Task Changes_WhileMonitoringIsPaused_AreNotQueuedUntilResumed()
    {
        await ImportAsync();
        monitoringPause.Pause();
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");

        await bed.Get<ChangeProcessor>().SubmitAsync(bed.Folder.Id, [await ChangedAsync("photo.jpg")], Token);
        clock.Advance(ChangeProcessor.GatherMax);
        await Task.Delay(500, Token);
        Assert.Empty(await ChangeJobsAsync());
        Assert.Empty(await bed.ItemsAsync());

        monitoringPause.Resume();
        await AdvanceUntilQueuedAsync(TimeSpan.FromSeconds(1));
        await bed.WaitUntilIdleAsync();
        Assert.Single(await bed.ItemsAsync());
    }

    [Fact]
    public async Task Changes_OfARemovedFolder_AreDropped()
    {
        await ImportAsync();
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        var change = await ChangedAsync("photo.jpg");
        await bed.Library.Folders.DeleteAsync(bed.Folder.Id, Token);

        await bed.Get<ChangeProcessor>().SubmitAsync(bed.Folder.Id, [change], Token);
        clock.Advance(ChangeProcessor.GatherQuiet);
        await Task.Delay(300, Token);
        await bed.WaitUntilIdleAsync();

        Assert.Empty(await ChangeJobsAsync());
        Assert.Empty(await bed.ItemsAsync());
    }

    [Fact]
    public async Task Burst_2000ChangesIn40Submits_MakeOneImportJob()
    {
        await ImportAsync();
        var processor = bed.Get<ChangeProcessor>();

        // A copy reports its files as they become stable, a few every half second.
        for (var submit = 0; submit < 40; submit++)
        {
            await processor.SubmitAsync(bed.Folder.Id, [.. Enumerable.Range(submit * 50, 50).Select(i => new FolderChange(FolderChangeKind.Changed, $"copy/IMG_{i:D4}.jpg"))], Token);
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Empty(await ChangeJobsAsync());
        await AdvanceUntilQueuedAsync(ChangeProcessor.GatherQuiet);
        await bed.WaitUntilIdleAsync();

        var job = Assert.Single(await ChangeJobsAsync());
        var work = await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token);
        Assert.Single(work, item => item.Stage == ChangeProcessingStages.Apply);
    }

    [Fact]
    public async Task Burst_ThatKeepsComing_IsQueuedAfterTheLongestGather_AndLaterChangesWaitForThatBatch()
    {
        await ImportAsync();
        var scheduler = bed.Jobs.Current!;
        scheduler.Pause();
        var processor = bed.Get<ChangeProcessor>();
        var step = TimeSpan.FromSeconds(1);

        for (var elapsed = TimeSpan.Zero; elapsed < ChangeProcessor.GatherMax; elapsed += step)
        {
            await processor.SubmitAsync(bed.Folder.Id, [new FolderChange(FolderChangeKind.Changed, $"copy/{elapsed.TotalSeconds}.jpg")], Token);
            clock.Advance(step);
        }

        await WaitForAsync(async () => (await ChangeJobsAsync()).Count == 1);
        var job = Assert.Single(await ChangeJobsAsync());

        // The first batch is still queued, so the next one waits: a folder's batches never run side by side.
        await processor.SubmitAsync(bed.Folder.Id, [new FolderChange(FolderChangeKind.Changed, "copy/last.jpg")], Token);
        clock.Advance(ChangeProcessor.GatherQuiet);
        await Task.Delay(300, Token);
        Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token));

        scheduler.Resume();
        await bed.WaitUntilIdleAsync();
        await AdvanceUntilQueuedAsync(ChangeProcessor.GatherQuiet);
        await bed.WaitUntilIdleAsync();

        var batches = (await Task.WhenAll((await ChangeJobsAsync()).Select(other => bed.Library.Jobs.GetWorkItemsAsync(other.Id, Token))))
            .SelectMany(items => items).Where(item => item.Stage == ChangeProcessingStages.Apply).OrderBy(item => item.CreatedAt).ToList();
        Assert.Equal(2, batches.Count);
        Assert.True(batches[1].CreatedAt >= batches[0].FinishedAt);
    }

    [Fact]
    public async Task Changes_AfterTheFoldersJobFinished_StartANewJob_LeavingTheFinishedOneFinished()
    {
        await ImportAsync();
        bed.Copy(SampleMedia.JpegWithExif, "a.jpg");
        await ApplyAsync(await ChangedAsync("a.jpg"));
        var first = Assert.Single(await ChangeJobsAsync());

        bed.Copy(SampleMedia.PngWithAlpha, "b.png");
        await ApplyAsync(await ChangedAsync("b.png"));

        var jobs = await ChangeJobsAsync();
        Assert.Equal(2, jobs.Count);
        Assert.Equal(ProcessingJobState.Completed, jobs.Single(job => job.Id == first.Id).State);
        Assert.Equal(first.FinishedAt, jobs.Single(job => job.Id == first.Id).FinishedAt);
    }

    // Submits a deletion and lets its grace period pass.
    private async Task DeleteAsync(string path)
    {
        await bed.Get<ChangeProcessor>().SubmitAsync(bed.Folder.Id, [new FolderChange(FolderChangeKind.Deleted, path)], Token);
        await ReleaseHeldAsync();
    }

    private async Task ReleaseHeldAsync()
    {
        await AdvanceUntilQueuedAsync(ChangeProcessor.DeleteGrace);
        await bed.WaitUntilIdleAsync();
    }

    // Deletions are held; the other changes are queued once the gathering is quiet.
    private async Task ApplyAsync(params FolderChange[] changes)
    {
        await bed.Get<ChangeProcessor>().SubmitAsync(bed.Folder.Id, changes, Token);
        if (changes.Any(change => change.Kind != FolderChangeKind.Deleted))
        {
            await AdvanceUntilQueuedAsync(ChangeProcessor.GatherQuiet);
        }

        await bed.WaitUntilIdleAsync();
    }

    // Lets time pass and waits for the job it queues. The earlier jobs have finished, so it's a new one.
    private async Task AdvanceUntilQueuedAsync(TimeSpan time)
    {
        var jobs = (await AllJobsAsync()).Count;
        clock.Advance(time);
        await WaitForAsync(async () => (await AllJobsAsync()).Count > jobs);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < end, "The changes weren't queued.");
            await Task.Delay(20, Token);
        }
    }

    private async Task<IReadOnlyList<ProcessingJob>> ChangeJobsAsync() =>
        [.. (await AllJobsAsync()).Where(job => job.Description.StartsWith("Import changes", StringComparison.Ordinal))];

    private async Task<IReadOnlyList<ProcessingJob>> AllJobsAsync() =>
        [.. (await bed.Library.Jobs.GetUnfinishedJobsAsync(Token)).Concat(await bed.Library.Jobs.GetRecentJobsAsync(100, Token)).DistinctBy(job => job.Id)];

    private async Task<FolderChange> ChangedAsync(string path) => new(FolderChangeKind.Changed, path, Stat: await StatAsync(path));

    private async Task<FolderChange> RenamedAsync(string oldPath, string path) => new(FolderChangeKind.Renamed, path, oldPath, await StatAsync(path));

    private async Task<MediaFileStat> StatAsync(string path) =>
        (await bed.Get<IMediaFileSource>().GetStatAsync(new FileLocation(bed.Folder.ToHandle(), path), Token))!;

    private void Move(string from, string to)
    {
        var target = Path.Combine(bed.Root, to);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(Path.Combine(bed.Root, from), target);
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

    // Indexed items for small files, added straight to the library: importing thousands of files is slow.
    private async Task<IReadOnlyList<MediaItem>> AddItemsAsync(string directory, int count)
    {
        Directory.CreateDirectory(Path.Combine(bed.Root, directory));
        var now = DateTimeOffset.UtcNow;
        var items = new List<MediaItem>(count);
        for (var i = 0; i < count; i++)
        {
            var path = $"{directory}/IMG_{i:D5}.jpg";
            var file = new FileInfo(Path.Combine(bed.Root, path));
            await File.WriteAllBytesAsync(file.FullName, BitConverter.GetBytes(i), Token);
            file.Refresh();
            var modified = DateTimeOffset.FromUnixTimeMilliseconds(new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds());
            var item = MediaItem.Discover(bed.Folder.Id, RelativePath.From(path), MediaType.Image, file.Length, modified, modified, (UInt128)i + 1, now);
            item.ChangeStatus(MediaItemStatus.Pending, now);
            item.ChangeStatus(MediaItemStatus.Indexed, now);
            items.Add(item);
        }

        foreach (var chunk in items.Chunk(500))
        {
            await bed.Library.Items.AddRangeAsync(chunk, Token);
        }

        return items;
    }

    private async Task<MediaItem> ExcludeAsync(string path)
    {
        var item = await ItemAsync(path);
        item.ChangeStatus(MediaItemStatus.Excluded, DateTimeOffset.UtcNow);
        await bed.Library.Items.UpdateAsync(item, Token);
        return item;
    }

    private async Task<MediaItem> ItemAsync(string path) => (await bed.ItemsAsync()).Single(item => item.Path.Value == path);
}
