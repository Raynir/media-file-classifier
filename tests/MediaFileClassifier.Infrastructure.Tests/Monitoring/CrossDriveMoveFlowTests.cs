using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Monitoring;

/// <summary>
/// A file moved between two watched folders on different drives arrives as a copy, then a delete (MFC-1052): the old
/// item keeps its ID and metadata, and the copy's item goes. The second folder stands for the other drive.
/// </summary>
public sealed class CrossDriveMoveFlowTests : IAsyncLifetime
{
    private readonly ManualClock clock = new(DateTimeOffset.UtcNow);
    private readonly TemporaryFolder otherDrive = new();
    private ImportTestBed bed = null!;
    private IndexedFolder other = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // The stages run on the manual clock too, so the copy window can pass.
        bed = await ImportTestBed.CreateAsync(configure: services => services
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton(provider => new ChangeProcessor(provider.GetRequiredService<HeldDeletes>(), clock, NullLogger<ChangeProcessor>.Instance, provider.GetRequiredService<IJobSystem>(), new MonitoringPause())));
        other = IndexedFolder.Create(bed.Library.Library.Id, otherDrive.Path, otherDrive.Path, LibraryTestBed.Start);
        await bed.Library.Folders.AddAsync(other, Token);
    }

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        otherDrive.Dispose();
    }

    [Fact]
    public async Task CopyThenDelete_KeepsTheOldItemAndDeletesTheCopyWithItsDerivedData()
    {
        var item = await ImportOriginalAsync();
        await CopyToOtherDriveAsync("photo.jpg");
        var copy = await OtherItemAsync("photo.jpg");
        await AddAiResultsAsync(copy.Id, "Lighthouse");
        var thumbnail = bed.Library.Layout.GetThumbnailPath(copy.Id, ThumbnailSize.Small);
        Assert.True(File.Exists(thumbnail));

        await DeleteOriginalAsync();

        var moved = Assert.Single(await AllItemsAsync());
        Assert.Equal((item.Id, other.Id, "photo.jpg"), (moved.Id, moved.FolderId, moved.Path.Value));
        Assert.NotEqual(MediaItemStatus.Missing, moved.Status);
        Assert.True(moved.IsFavorite);
        Assert.Contains((await bed.Library.Metadata.GetAsync(item.Id, Token)).Tags, tag => tag.Tag.Value == "holiday");
        Assert.Null(await bed.Library.Items.GetAsync(copy.Id, Token));
        Assert.False(File.Exists(thumbnail));
        Assert.Empty(await bed.Library.Embeddings.GetAsync(copy.Id, Token));
        var metadata = await bed.Library.Metadata.GetAsync(copy.Id, Token);
        Assert.Empty(metadata.Runs);
        Assert.Empty(metadata.AiValues);
    }

    [Fact]
    public async Task CopyThenDelete_CopyRegisteredBeforeTheWindow_IsNotPaired()
    {
        var item = await ImportOriginalAsync();
        await CopyToOtherDriveAsync("photo.jpg");
        var copy = await OtherItemAsync("photo.jpg");
        clock.Advance(ApplyChangesStage.CopyWindow);

        await DeleteOriginalAsync();

        Assert.Equal(MediaItemStatus.Missing, (await bed.Library.Items.GetAsync(item.Id, Token))!.Status);
        Assert.NotNull(await bed.Library.Items.GetAsync(copy.Id, Token));
    }

    [Fact]
    public async Task CopyThenDelete_DuplicateTheImportFound_IsNotPaired()
    {
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        bed.Copy(SampleMedia.JpegWithExif, "kept.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var kept = (await bed.ItemsAsync()).Single(found => found.Path.Value == "kept.jpg");

        await DeleteOriginalAsync();

        Assert.Equal(MediaItemStatus.Missing, (await bed.ItemsAsync()).Single(found => found.Path.Value == "photo.jpg").Status);
        Assert.Equal("kept.jpg", (await bed.Library.Items.GetAsync(kept.Id, Token))!.Path.Value);
    }

    [Fact]
    public async Task CopyThenDelete_CopyNotFingerprintedYet_IsPaired()
    {
        var item = await ImportOriginalAsync();
        await CopyToOtherDriveAsync("photo.jpg");
        var copy = await OtherItemAsync("photo.jpg");
        copy.SetHashes(copy.Hashes with { Fingerprint = null, ContentHash = null });
        await bed.Library.Items.UpdateAsync(copy, Token);

        await DeleteOriginalAsync();

        var moved = Assert.Single(await AllItemsAsync());
        Assert.Equal((item.Id, other.Id, "photo.jpg"), (moved.Id, moved.FolderId, moved.Path.Value));
        Assert.Null(await bed.Library.Items.GetAsync(copy.Id, Token));
    }

    [Fact]
    public async Task CopyThenDelete_TwoCopies_TheOneWithTheSameNameIsPaired()
    {
        var item = await ImportOriginalAsync();
        await CopyToOtherDriveAsync("photo.jpg", "backup.jpg");
        var sameName = await OtherItemAsync("photo.jpg");
        var otherName = await OtherItemAsync("backup.jpg");

        await DeleteOriginalAsync();

        var items = await AllItemsAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal("photo.jpg", items.Single(found => found.Id == item.Id).Path.Value);
        Assert.Null(await bed.Library.Items.GetAsync(sameName.Id, Token));
        Assert.NotNull(await bed.Library.Items.GetAsync(otherName.Id, Token));
    }

    [Fact]
    public async Task CopyThenDelete_TwoCopiesWithOtherNames_PairsNeither()
    {
        var item = await ImportOriginalAsync();
        await CopyToOtherDriveAsync("first.jpg", "second.jpg");

        await DeleteOriginalAsync();

        Assert.Equal(3, (await AllItemsAsync()).Count);
        Assert.Equal(MediaItemStatus.Missing, (await bed.Library.Items.GetAsync(item.Id, Token))!.Status);
    }

    private async Task<MediaItem> ImportOriginalAsync()
    {
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var item = Assert.Single(await bed.ItemsAsync());
        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.AddTag(TagName.From("holiday"), DateTimeOffset.UtcNow), Token);
        item.SetFavorite(true, DateTimeOffset.UtcNow);
        await bed.Library.Items.UpdateAsync(item, Token);
        return item;
    }

    // The copies as the watcher reports them: imported into the other folder through the change processing.
    private async Task CopyToOtherDriveAsync(params string[] paths)
    {
        var changes = new List<FolderChange>();
        foreach (var path in paths)
        {
            CopyFile(path);
            var stat = await bed.Get<IMediaFileSource>().GetStatAsync(new FileLocation(other.ToHandle(), path), Token);
            changes.Add(new FolderChange(FolderChangeKind.Changed, path, Stat: stat));
        }

        await bed.Get<ChangeProcessor>().SubmitAsync(other.Id, changes, Token);
        await AdvanceUntilQueuedAsync(ChangeProcessor.GatherQuiet);
        await bed.WaitUntilIdleAsync();
    }

    private string CopyFile(string path)
    {
        var target = Path.Combine(otherDrive.Path, path);
        File.Copy(Path.Combine(bed.Root, "photo.jpg"), target);
        return target;
    }

    private async Task DeleteOriginalAsync()
    {
        File.Delete(Path.Combine(bed.Root, "photo.jpg"));
        await bed.Get<ChangeProcessor>().SubmitAsync(bed.Folder.Id, [new FolderChange(FolderChangeKind.Deleted, "photo.jpg")], Token);
        await AdvanceUntilQueuedAsync(ChangeProcessor.DeleteGrace);
        await bed.WaitUntilIdleAsync();
    }

    private async Task<MediaItem> OtherItemAsync(string path) =>
        (await AllItemsAsync()).Single(item => item.FolderId == other.Id && item.Path.Value == path);

    private async Task<IReadOnlyList<MediaItem>> AllItemsAsync() => await bed.Library.Items.GetPageAsync(0, 1000, Token);

    private async Task AdvanceUntilQueuedAsync(TimeSpan time)
    {
        var jobs = (await AllJobsAsync()).Count;
        clock.Advance(time);
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while ((await AllJobsAsync()).Count <= jobs)
        {
            Assert.True(DateTime.UtcNow < end, "The changes weren't queued.");
            await Task.Delay(20, Token);
        }
    }

    private async Task<IReadOnlyList<Domain.Jobs.ProcessingJob>> AllJobsAsync() =>
        [.. (await bed.Library.Jobs.GetUnfinishedJobsAsync(Token)).Concat(await bed.Library.Jobs.GetRecentJobsAsync(100, Token)).DistinctBy(job => job.Id)];

    private async Task AddAiResultsAsync(Id<MediaItem> itemId, string title)
    {
        var model = bed.LibraryBed.Model;
        var now = LibraryTestBed.Start;
        var run = AnalysisRun.Start(itemId, AnalyzerKeys.Describer, model, "p1", now);
        run.Complete(now);
        await bed.Library.AnalyzerStates.ApplyRunAsync(
            run,
            [AiFieldValue.For(run, DescriptiveField.Title, FieldValue.FromText(title), null, now)],
            [TagAssignment.Ai(run, TagName.From("lamp"), null, now)],
            1,
            AnalyzerState.Done(itemId, AnalyzerKeys.Describer, run.Id, now),
            Token);
        await bed.Library.Embeddings.ReplaceAsync(itemId, model.Id, [Embedding.Create(itemId, model, EmbeddingKind.Image, [0.5f, 0.5f, 0.5f, 0.5f], now)], Token);
    }
}
