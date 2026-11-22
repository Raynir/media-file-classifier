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
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync(configure: services =>
        services.AddSingleton(provider => new ChangeProcessor(provider.GetRequiredService<HeldDeletes>(), clock, NullLogger<ChangeProcessor>.Instance, provider.GetRequiredService<IJobSystem>())));

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
        await Task.Delay(500, Token);
        Assert.Empty(await bed.ItemsAsync());

        scheduler.Resume();
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

        await ApplyAsync(change);

        Assert.Empty(await bed.ItemsAsync());
    }

    // Submits a deletion and lets its grace period pass.
    private async Task DeleteAsync(string path)
    {
        await bed.Get<ChangeProcessor>().SubmitAsync(bed.Folder.Id, [new FolderChange(FolderChangeKind.Deleted, path)], Token);
        await ReleaseHeldAsync();
    }

    private async Task ReleaseHeldAsync()
    {
        var jobs = (await bed.Library.Jobs.GetRecentJobsAsync(100, Token)).Count;
        clock.Advance(ChangeProcessor.DeleteGrace);
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while ((await bed.Library.Jobs.GetRecentJobsAsync(100, Token)).Count == jobs)
        {
            Assert.True(DateTime.UtcNow < end, "The held deletions weren't queued.");
            await Task.Delay(20, Token);
        }

        await bed.WaitUntilIdleAsync();
    }

    private async Task ApplyAsync(params FolderChange[] changes)
    {
        await bed.Get<ChangeProcessor>().SubmitAsync(bed.Folder.Id, changes, Token);
        await bed.WaitUntilIdleAsync();
    }

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

    private async Task<MediaItem> ItemAsync(string path) => (await bed.ItemsAsync()).Single(item => item.Path.Value == path);
}
