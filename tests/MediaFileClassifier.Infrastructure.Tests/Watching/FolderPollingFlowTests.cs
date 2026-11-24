using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Testing.Platform;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Rescan;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Watching;

/// <summary>
/// Monitoring modes with a real library and job system (E04-S05): a folder on a network share is polled rather than
/// watched, and its new files are imported within one poll interval; a folder in Manual mode is left to the user's
/// rescans.
/// </summary>
public sealed class FolderPollingFlowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NetworkFolder_NewFile_IsImportedWithinOnePollInterval_WithoutAWatcher()
    {
        var probe = new FakeFolderLocationProbe();
        var factory = new CountingWatcherFactory();
        await using var bed = await ImportTestBed.CreateAsync(configure: services => services
            .AddSingleton<IFolderLocationProbe>(probe)
            .AddSingleton<IFolderWatcherFactory>(factory));
        probe.Network(bed.Root);
        bed.Copy(SampleMedia.JpegWithExif, "kept.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var watchers = bed.Get<FolderWatchManager>();
        watchers.Start();
        await watchers.RefreshAsync();
        var clock = new ManualClock();
        using var poller = new FolderPoller(
            bed.Get<ILibrarySession>(),
            bed.Get<FolderLocations>(),
            bed.Sender,
            bed.Get<IPublisher>(),
            bed.Store,
            clock,
            NullLogger<FolderPoller>.Instance);
        poller.Start();
        await poller.RefreshAsync();

        bed.Copy(SampleMedia.WebP, "new.webp");
        clock.Advance(poller.Interval);
        await poller.WaitForPollsAsync();
        await bed.WaitUntilIdleAsync();

        // The library bed's own local folder is watched; the share isn't.
        Assert.DoesNotContain(bed.Folder.Id, watchers.WatchedFolders);
        Assert.Equal(watchers.WatchedFolders.Count, factory.Started);
        Assert.Equal(["kept.jpg", "new.webp"], (await bed.ItemsAsync()).Select(item => item.Path.Value).Order(StringComparer.Ordinal));
        var job = Assert.Single(await bed.Library.Jobs.GetRecentJobsAsync(10, Token), job => job.Type == RescanStages.QuickSyncJob);
        Assert.Equal(bed.Folder.Id, job.FolderId);
    }

    [Fact]
    public async Task ManualFolder_IsLeftOutOfQuickSyncs_ButARescanStillFindsItsFiles()
    {
        await using var bed = await ImportTestBed.CreateAsync();
        bed.Copy(SampleMedia.JpegWithExif, "kept.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var folder = (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!;
        folder.ChangeMonitoringMode(MonitoringMode.Manual);
        await bed.Library.Folders.UpdateAsync(folder, Token);
        bed.Copy(SampleMedia.WebP, "new.webp");

        // The library-wide sync lists the library bed's other folder only.
        await bed.Sender.Send(new StartRescanCommand(Quick: true), Token);
        await bed.WaitUntilIdleAsync();
        var single = await bed.Sender.Send(new StartRescanCommand(folder.Id, Quick: true), Token);
        await bed.WaitUntilIdleAsync();
        Assert.Equal(StartRescanOutcome.NothingToScan, single.Outcome);
        Assert.Single(await bed.ItemsAsync());

        var rescan = await bed.Sender.Send(new StartRescanCommand(folder.Id), Token);
        await bed.WaitUntilIdleAsync();
        Assert.Equal(StartRescanOutcome.Started, rescan.Outcome);
        Assert.Equal(2, (await bed.ItemsAsync()).Count);
    }

    private sealed class CountingWatcherFactory : IFolderWatcherFactory
    {
        private int started;

        public int Started => Volatile.Read(ref started);

        public IFolderWatcher Watch(Infrastructure.Abstractions.Files.FolderHandle folder)
        {
            Interlocked.Increment(ref started);
            return new Watcher();
        }

        private sealed class Watcher : IFolderWatcher
        {
            public event EventHandler<FolderWatchEvent>? Changed
            {
                add { }
                remove { }
            }

            public event EventHandler<FolderWatchErrorEventArgs>? Failed
            {
                add { }
                remove { }
            }

            public void Dispose()
            {
            }
        }
    }
}
