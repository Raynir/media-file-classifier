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
/// Recovery from lost watcher events with a real library and job system (E04-S01-T04): an overflow rescans the folder,
/// so the files whose events were lost are all found.
/// </summary>
public sealed class FolderWatchRecoveryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Overflow_RescansTheFolder_WithNoMissedFiles()
    {
        var factory = new LosingWatcherFactory();
        await using var bed = await ImportTestBed.CreateAsync(configure: services => services.AddSingleton<IFolderWatcherFactory>(factory));
        bed.Copy(SampleMedia.JpegWithExif, "old.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var manager = bed.Get<FolderWatchManager>();
        manager.Start();
        await manager.RefreshAsync();
        Assert.Contains(bed.Folder.Id, manager.WatchedFolders);

        // Files arrive while every event is lost.
        bed.Copy(SampleMedia.PngWithAlpha, "new/alpha.png");
        bed.Copy(SampleMedia.WebP, "webp.webp");
        bed.WriteDistinctPhotos(3, "burst");
        factory.Overflow(bed.Folder.PlatformHandle);
        await manager.WaitForRecoveryAsync();
        await bed.WaitUntilIdleAsync();

        var paths = (await bed.ItemsAsync()).Select(item => item.Path.Value).Order(StringComparer.Ordinal);
        Assert.Equal(["burst/IMG_00000.jpg", "burst/IMG_00001.jpg", "burst/IMG_00002.jpg", "new/alpha.png", "old.jpg", "webp.webp"], paths);
        var rescan = Assert.Single(await bed.Library.Jobs.GetRecentJobsAsync(10, Token), job => job.Type == RescanStages.RescanJob && job.FolderId == bed.Folder.Id);
        var summary = await bed.Sender.Send(new GetRescanSummaryQuery(rescan.Id), Token);
        Assert.Equal(5, summary!.Counts!.New);
        Assert.Equal(1, summary.Counts.Unchanged);
    }

    /// <summary>
    /// Watchers that lose every event, and overflow when told.
    /// </summary>
    private sealed class LosingWatcherFactory : IFolderWatcherFactory
    {
        private readonly List<Watcher> watchers = [];

        public IFolderWatcher Watch(FolderHandle folder)
        {
            var watcher = new Watcher(folder.Id);
            lock (watchers)
            {
                watchers.Add(watcher);
            }

            return watcher;
        }

        public void Overflow(string folder)
        {
            lock (watchers)
            {
                foreach (var watcher in watchers.Where(watcher => watcher.Folder == folder))
                {
                    watcher.Overflow();
                }
            }
        }

        private sealed class Watcher(string folder) : IFolderWatcher
        {
            public string Folder { get; } = folder;

#pragma warning disable CS0067 // Every event is lost.
            public event EventHandler<FolderWatchEvent>? Changed;
#pragma warning restore CS0067

            public event EventHandler<FolderWatchErrorEventArgs>? Failed;

            public void Overflow() => Failed?.Invoke(this, new FolderWatchErrorEventArgs(true, new InternalBufferOverflowException()));

            public void Dispose()
            {
            }
        }
    }
}
