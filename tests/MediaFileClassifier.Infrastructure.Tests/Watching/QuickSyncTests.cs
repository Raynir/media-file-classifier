using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Rescan;
using MediaFileClassifier.UseCases.Rescan.GetRescanSummary;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Watching;

/// <summary>
/// The quick sync on library open (E04-S03-T01) with a real library and job system: the changes made while the app was
/// closed are found once the watchers run.
/// </summary>
public sealed class QuickSyncTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Open_FindsFilesAddedWhileClosed_AndMarksTheOnesRemoved()
    {
        await using var bed = await ImportTestBed.CreateAsync(configure: services => services.AddSingleton<IFolderWatcherFactory>(new QuietWatcherFactory()));
        bed.Copy(SampleMedia.JpegWithExif, "kept.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "gone.png");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();

        // The app is closed; the folder changes.
        File.Delete(Path.Combine(bed.Root, "gone.png"));
        bed.Copy(SampleMedia.WebP, "added/new.webp");
        bed.WriteDistinctPhotos(2, "burst");

        var manager = bed.Get<FolderWatchManager>();
        manager.Start();
        using var starter = bed.Get<QuickSyncStarter>();
        starter.Start();
        await starter.WaitAsync();
        await bed.WaitUntilIdleAsync();

        var items = await bed.ItemsAsync();
        Assert.Equal(["added/new.webp", "burst/IMG_00000.jpg", "burst/IMG_00001.jpg", "gone.png", "kept.jpg"], items.Select(item => item.Path.Value).Order(StringComparer.Ordinal));
        Assert.Equal(MediaFileClassifier.Domain.Media.MediaItemStatus.Missing, items.Single(item => item.Path.Value == "gone.png").Status);
        var job = Assert.Single(await bed.Library.Jobs.GetRecentJobsAsync(10, Token), job => job.Type == RescanStages.QuickSyncJob);
        Assert.Equal((int)MediaFileClassifier.Domain.Jobs.JobPriority.Import, job.Priority);
        var counts = (await bed.Sender.Send(new GetRescanSummaryQuery(job.Id), Token))!.Counts!;
        Assert.Equal((3, 0, 1), (counts.New, counts.Moved, counts.Missing));
        Assert.Equal(1, counts.Unchanged);
    }

    [Fact]
    public async Task Open_StartsAfterTheWatchersRun()
    {
        await using var bed = await ImportTestBed.CreateAsync(configure: services => services.AddSingleton<IFolderWatcherFactory>(new QuietWatcherFactory()));
        bed.Copy(SampleMedia.JpegWithExif, "a.jpg");
        var manager = bed.Get<FolderWatchManager>();
        manager.Start();
        using var starter = bed.Get<QuickSyncStarter>();

        starter.Start();
        await starter.WaitAsync();

        // The watcher was running when the sync was queued.
        Assert.Contains(bed.Folder.Id, manager.WatchedFolders);
        await bed.WaitUntilIdleAsync();
        Assert.Single(await bed.Library.Jobs.GetRecentJobsAsync(10, Token), job => job.Type == RescanStages.QuickSyncJob);
    }

    [Fact]
    public async Task Open_WithTheSwitchOff_QueuesNothing()
    {
        await using var bed = await ImportTestBed.CreateAsync();
        bed.Copy(SampleMedia.JpegWithExif, "a.jpg");
        bed.Store.SetValue(MonitoringSettings.QuickSyncOnOpen, false);
        using var starter = bed.Get<QuickSyncStarter>();

        starter.Start();
        await starter.WaitAsync();
        await bed.WaitUntilIdleAsync();

        Assert.Empty(await bed.Library.Jobs.GetRecentJobsAsync(10, Token));
        Assert.Empty(await bed.ItemsAsync());
    }

    [Fact]
    public async Task Open_OfAnUnchangedLibrary_FindsNothingToDo()
    {
        await using var bed = await ImportTestBed.CreateAsync();
        bed.WriteDistinctPhotos(3);
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        using var starter = bed.Get<QuickSyncStarter>();

        starter.Start();
        await starter.WaitAsync();
        await bed.WaitUntilIdleAsync();

        var job = Assert.Single(await bed.Library.Jobs.GetRecentJobsAsync(10, Token), job => job.Type == RescanStages.QuickSyncJob);
        var counts = (await bed.Sender.Send(new GetRescanSummaryQuery(job.Id), Token))!.Counts!;
        Assert.Equal((0, 0, 0, 0, 3), (counts.New, counts.Moved, counts.Missing, counts.Modified, counts.Unchanged));
    }

    /// <summary>
    /// Watchers that are running and see nothing, as when the folder changed before they started.
    /// </summary>
    private sealed class QuietWatcherFactory : IFolderWatcherFactory
    {
        public IFolderWatcher Watch(FolderHandle folder)
        {
            return new Watcher();
        }

        private sealed class Watcher : IFolderWatcher
        {
#pragma warning disable CS0067 // Nothing is ever seen.
            public event EventHandler<FolderWatchEvent>? Changed;

            public event EventHandler<FolderWatchErrorEventArgs>? Failed;
#pragma warning restore CS0067

            public void Dispose()
            {
            }
        }
    }
}
