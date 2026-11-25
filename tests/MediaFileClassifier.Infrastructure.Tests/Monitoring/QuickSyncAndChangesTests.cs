using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Discovery;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Monitoring;

/// <summary>
/// The quick sync on open and the watcher's change processing both see a file that arrives while the sync lists the
/// folders (MFC-1041): it ends as one item, and neither fails on the unique (folder, path) index.
/// </summary>
public sealed class QuickSyncAndChangesTests : IAsyncLifetime
{
    private readonly ManualClock clock = new(DateTimeOffset.UtcNow);
    private readonly PausingDiscovery discovery = new();
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync(configure: services => services
            .AddSingleton<FolderDiscovery>()
            .AddSingleton<IFolderDiscovery>(provider => discovery.Wrap(provider.GetRequiredService<FolderDiscovery>()))
            .AddSingleton(provider => new ChangeProcessor(provider.GetRequiredService<HeldDeletes>(), clock, NullLogger<ChangeProcessor>.Instance, provider.GetRequiredService<IJobSystem>(), new MonitoringPause())));
        bed.Copy(SampleMedia.JpegWithExif, "old.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task FileArrivingWhileTheSyncLists_IsOneItem()
    {
        bed.Copy(SampleMedia.PngWithAlpha, "new.png");
        discovery.PauseNextSnapshot();
        var sync = await bed.Sender.Send(new StartRescanCommand(Quick: true), Token);
        Assert.Equal(StartRescanOutcome.Started, sync.Outcome);
        Assert.True(await discovery.Paused.WaitAsync(TimeSpan.FromSeconds(30), Token));

        // The watcher reports the file while the sync's listing, which has it too, is still under way.
        var stat = await bed.Get<IMediaFileSource>().GetStatAsync(new FileLocation(bed.Folder.ToHandle(), "new.png"), Token);
        await bed.Get<ChangeProcessor>().SubmitAsync(bed.Folder.Id, [new FolderChange(FolderChangeKind.Changed, "new.png", Stat: stat)], Token);
        clock.Advance(ChangeProcessor.GatherQuiet);
        await WaitForChangeJobAsync();
        await Task.Delay(200, Token);
        discovery.Resume();
        await bed.WaitUntilIdleAsync();

        Assert.Single(await bed.ItemsAsync(), item => item.Path.Value == "new.png");
        Assert.Equal(2, (await bed.ItemsAsync()).Count);
        var jobs = await bed.Library.Jobs.GetRecentJobsAsync(20, Token);
        Assert.All(jobs, job => Assert.Equal(ProcessingJobState.Completed, job.State));
        foreach (var job in jobs)
        {
            Assert.DoesNotContain(await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token), item => item.State != WorkItemState.Completed);
        }
    }

    private async Task WaitForChangeJobAsync()
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!(await bed.Library.Jobs.GetUnfinishedJobsAsync(Token)).Any(job => job.Description.StartsWith("Import changes", StringComparison.Ordinal)))
        {
            Assert.True(DateTime.UtcNow < end, "The change wasn't queued.");
            await Task.Delay(20, Token);
        }
    }

    // Holds the next folder snapshot, once it's been taken, until Resume: the sync has listed the folder but not
    // compared it with the library yet.
    private sealed class PausingDiscovery
    {
        private readonly TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int armed;

        public SemaphoreSlim Paused { get; } = new(0);

        public IFolderDiscovery Wrap(IFolderDiscovery inner) => new Wrapper(this, inner);

        public void PauseNextSnapshot() => armed = 1;

        public void Resume() => resumed.TrySetResult();

        private sealed class Wrapper(PausingDiscovery owner, IFolderDiscovery inner) : IFolderDiscovery
        {
            public Task<DiscoveryResult> DiscoverAsync(IOpenLibrary library, IndexedFolder folder, IProgress<DiscoveryProgress>? progress = null, Func<IReadOnlyList<Domain.Media.MediaItem>, CancellationToken, Task>? registered = null, CancellationToken cancellationToken = default) =>
                inner.DiscoverAsync(library, folder, progress, registered, cancellationToken);

            public Task<IReadOnlyList<FileRegistration>> RegisterFilesAsync(IOpenLibrary library, IndexedFolder folder, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default) =>
                inner.RegisterFilesAsync(library, folder, relativePaths, cancellationToken);

            public async Task<FolderSnapshot> SnapshotAsync(IOpenLibrary library, IndexedFolder folder, IProgress<DiscoveryProgress>? progress = null, CancellationToken cancellationToken = default)
            {
                var snapshot = await inner.SnapshotAsync(library, folder, progress, cancellationToken).ConfigureAwait(false);
                if (Interlocked.Exchange(ref owner.armed, 0) == 1)
                {
                    owner.Paused.Release();
                    await owner.resumed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                return snapshot;
            }
        }
    }
}
