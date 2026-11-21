using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Maintenance;
using MediaFileClassifier.UseCases.Maintenance.RegenerateThumbnails;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Maintenance;

/// <summary>
/// Regenerating thumbnails with a real library, job system and thumbnail cache (E02-S06-T01, T02).
/// </summary>
public sealed class RegenerateThumbnailsCommandTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync();
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "b/alpha.png");
        bed.Copy(SampleMedia.WebP, "webp.webp");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task MissingOnly_ThumbnailsFolderDeleted_RestoresEveryThumbnail_WithoutChangingTheItems()
    {
        var before = await bed.ItemsAsync();
        Directory.Delete(bed.Library.Layout.ThumbnailsDirectory, recursive: true);

        var result = await bed.Sender.Send(new RegenerateThumbnailsCommand(), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(RegenerateThumbnailsOutcome.Started, result.Outcome);
        Assert.Equal(3, result.ItemCount);
        Assert.Equal(ProcessingJobState.Completed, (await bed.Library.Jobs.GetJobAsync(result.JobId!.Value, Token))!.State);
        Assert.All(before, item => Assert.False(RegenerateThumbnailStage.NeedsThumbnails(bed.Get<IThumbnailCache>(), bed.Library.Layout, item.Id, item.FileModifiedAt)));
        var after = await bed.ItemsAsync();
        Assert.Equal(
            before.Select(item => (item.Id, item.Status, item.StatusChangedAt)).OrderBy(item => item.Id.Value),
            after.Select(item => (item.Id, item.Status, item.StatusChangedAt)).OrderBy(item => item.Id.Value));
    }

    [Fact]
    public async Task MissingOnly_QueuesOnlyTheItemsMissingAThumbnail()
    {
        var items = await bed.ItemsAsync();
        var target = items[0];
        File.Delete(bed.Library.Layout.GetThumbnailPath(target.Id, ThumbnailSize.Large));

        var result = await bed.Sender.Send(new RegenerateThumbnailsCommand(), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(1, result.ItemCount);
        Assert.True(File.Exists(bed.Library.Layout.GetThumbnailPath(target.Id, ThumbnailSize.Large)));
    }

    [Fact]
    public async Task MissingOnly_EveryThumbnailThere_QueuesNothing()
    {
        var result = await bed.Sender.Send(new RegenerateThumbnailsCommand(), Token);

        Assert.Equal(RegenerateThumbnailsOutcome.NothingToDo, result.Outcome);
        Assert.Null(result.JobId);
    }

    [Fact]
    public async Task Regenerate_WhileAThumbnailJobIsUnfinished_IsBusy()
    {
        Directory.Delete(bed.Library.Layout.ThumbnailsDirectory, recursive: true);
        var unfinished = new ProcessingJob
        {
            Id = Id<ProcessingJob>.New(),
            Type = MaintenanceJobs.ThumbnailsJob,
            Description = "Regenerate thumbnails",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await bed.Library.Jobs.AddJobAsync(unfinished, [], Token);

        var result = await bed.Sender.Send(new RegenerateThumbnailsCommand(), Token);

        Assert.Equal(RegenerateThumbnailsOutcome.Busy, result.Outcome);
    }

    [Fact]
    public async Task Stage_ItemMissing_IsSkipped()
    {
        var items = await bed.ItemsAsync();
        var target = items[0];
        target.ChangeStatus(MediaItemStatus.Missing, DateTimeOffset.UtcNow);
        await bed.Library.Items.UpdateAsync(target, Token);
        Directory.Delete(bed.Library.Layout.ThumbnailsDirectory, recursive: true);

        var result = await bed.Sender.Send(new RegenerateThumbnailsCommand(), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(2, result.ItemCount);
        Assert.False(File.Exists(bed.Library.Layout.GetThumbnailPath(target.Id, ThumbnailSize.Small)));
    }

    [Fact]
    public async Task All_DeletesTheCacheAndRemakesEveryThumbnail_KeepingKeyframePictures()
    {
        var items = await bed.ItemsAsync();
        var layout = bed.Library.Layout;
        var keyframe = layout.GetKeyframeThumbnailPath(items[0].Id, 0);
        await File.WriteAllBytesAsync(keyframe, [1, 2, 3], Token);
        var orphan = layout.GetThumbnailPath(Id<MediaItem>.New(), ThumbnailSize.Small);
        Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
        await File.WriteAllBytesAsync(orphan, [1, 2, 3], Token);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var item in items)
        {
            File.SetLastWriteTimeUtc(layout.GetThumbnailPath(item.Id, ThumbnailSize.Small), stamp);
        }

        var result = await bed.Sender.Send(new RegenerateThumbnailsCommand(All: true), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(RegenerateThumbnailsOutcome.Started, result.Outcome);
        Assert.Equal(3, result.ItemCount);
        Assert.All(items, item => Assert.True(File.GetLastWriteTimeUtc(layout.GetThumbnailPath(item.Id, ThumbnailSize.Small)) > stamp));
        Assert.All(items, item => Assert.True(File.Exists(layout.GetThumbnailPath(item.Id, ThumbnailSize.Large))));
        Assert.False(File.Exists(orphan));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(keyframe, Token));
    }

    [Fact]
    public async Task All_ItemsOnScreenGoFirst()
    {
        var generated = new System.Collections.Concurrent.ConcurrentQueue<Id<MediaItem>>();
        await using var recording = await ImportTestBed.CreateAsync(
            cpu: 1,
            configure: services => services.AddSingleton<IThumbnailCache>(
                new RecordingThumbnailCache(new ThumbnailCache(TimeProvider.System, NullLogger<ThumbnailCache>.Instance), generated)));
        recording.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        recording.Copy(SampleMedia.PngWithAlpha, "b/alpha.png");
        recording.Copy(SampleMedia.WebP, "webp.webp");
        recording.Copy(SampleMedia.StaticGif, "still.gif");
        await recording.ImportAsync();
        await recording.WaitUntilIdleAsync();
        var onScreen = (await recording.ItemsAsync()).OrderBy(item => item.Id.Value).First().Id;
        generated.Clear();
        recording.Jobs.Current!.Boost([onScreen]);

        await recording.Sender.Send(new RegenerateThumbnailsCommand(All: true), Token);
        await recording.WaitUntilIdleAsync();

        Assert.Equal(4, generated.Count);
        Assert.Equal(onScreen, generated.First());
    }

    private sealed class RecordingThumbnailCache(ThumbnailCache inner, System.Collections.Concurrent.ConcurrentQueue<Id<MediaItem>> generated) : IThumbnailCache
    {
        public string? Find(Infrastructure.Abstractions.Libraries.LibraryLayout library, Id<MediaItem> itemId, ThumbnailSize size, DateTimeOffset sourceModifiedAt) =>
            inner.Find(library, itemId, size, sourceModifiedAt);

        public Task<ThumbnailOutcome> GenerateAsync(
            Infrastructure.Abstractions.Libraries.LibraryLayout library,
            Infrastructure.Abstractions.Metadata.MediaContent content,
            Infrastructure.Abstractions.Formats.MediaFormatDetection format,
            Domain.Metadata.TechnicalMetadata metadata,
            CancellationToken cancellationToken)
        {
            generated.Enqueue(content.ItemId);
            return inner.GenerateAsync(library, content, format, metadata, cancellationToken);
        }

        public void Invalidate(Infrastructure.Abstractions.Libraries.LibraryLayout library, Id<MediaItem> itemId) => inner.Invalidate(library, itemId);

        public int Clear(Infrastructure.Abstractions.Libraries.LibraryLayout library, CancellationToken cancellationToken = default) => inner.Clear(library, cancellationToken);
    }
}
