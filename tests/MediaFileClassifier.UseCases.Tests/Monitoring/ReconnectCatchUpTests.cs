using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Platform;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Rescan;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediaFileClassifier.UseCases.Tests.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Monitoring;

/// <summary>
/// Tests for <see cref="FoldersReconnectedHandler"/> and <see cref="ReconnectCatchUp"/> (E04-S04-T02): folders that come
/// back Online get a quick sync each, retried while busy as long as the folder is still Online and watched.
/// </summary>
public sealed class ReconnectCatchUpTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reconnected_QueuesAQuickSyncOfEachFolder()
    {
        using var bed = await Bed.CreateAsync();
        var first = await bed.AddFolderAsync(@"E:\Photos");
        var second = await bed.AddFolderAsync(@"E:\Videos");

        await bed.ReconnectAsync(first, second);

        Assert.Equivalent(new[] { first.Id, second.Id }, bed.Sender.Syncs.Select(sync => sync.FolderId!.Value).ToArray(), strict: true);
        Assert.All(bed.Sender.Syncs, sync => Assert.True(sync.Quick));
    }

    [Fact]
    public async Task Reconnected_RunsWhateverTheQuickSyncOnOpenSwitchSays()
    {
        using var bed = await Bed.CreateAsync();
        bed.Settings.IsLibraryOpen = true;
        bed.Settings.SetValue(Infrastructure.Abstractions.Settings.KnownSettings.QuickSyncOnOpen, false);
        var folder = await bed.AddFolderAsync(@"E:\Photos");

        await bed.ReconnectAsync(folder);

        Assert.Single(bed.Sender.Syncs);
    }

    [Fact]
    public async Task Reconnected_FolderOfflineAgainBeforeTheStart_IsNotSynced()
    {
        using var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        folder.MarkOffline(Now);
        bed.Library.FolderList.Seed(folder);

        await bed.ReconnectAsync(folder);

        Assert.Empty(bed.Sender.Syncs);
    }

    [Fact]
    public async Task Reconnected_WithWatchers_StartsOnceTheFolderIsWatched()
    {
        using var bed = await Bed.CreateAsync(watched: true);
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        bed.Sender.OnSync = () => bed.WatchedAtSync = bed.Manager.WatchedFolders.Contains(folder.Id);

        await bed.ReconnectAsync(folder);

        Assert.Single(bed.Sender.Syncs);
        Assert.True(bed.WatchedAtSync);
    }

    [Fact]
    public async Task Reconnected_WithWatchers_PolledFoldersAreSyncedThoughNotWatched()
    {
        using var bed = await Bed.CreateAsync(watched: true);
        bed.Probe.Network(@"\\nas\photos");
        var share = await bed.AddFolderAsync(@"\\nas\photos");
        var polled = await bed.AddFolderAsync(@"E:\Polled");
        polled.ChangeMonitoringMode(MonitoringMode.Poll);
        bed.Library.FolderList.Seed(polled);

        await bed.ReconnectAsync(share, polled);

        Assert.Empty(bed.Manager.WatchedFolders);
        Assert.Equivalent(new[] { share.Id, polled.Id }, bed.Sender.Syncs.Select(sync => sync.FolderId!.Value).ToArray(), strict: true);
    }

    [Fact]
    public async Task Reconnected_WhileTheFolderIsBusy_IsRetriedEvery15Seconds_UntilASyncStarts()
    {
        using var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        bed.Sender.Outcomes.Enqueue(StartRescanOutcome.Busy);
        bed.Sender.Outcomes.Enqueue(StartRescanOutcome.Busy);

        await bed.Handler.Handle(new FoldersReconnected([folder.Id]), Token);
        await WaitUntilAsync(() => bed.Sender.Syncs.Count() == 1);

        // A second notification while one waits doesn't ask twice.
        await bed.Handler.Handle(new FoldersReconnected([folder.Id]), Token);
        bed.Clock.Advance(ReconnectCatchUp.BusyRetry - TimeSpan.FromSeconds(1));
        await Task.Delay(50, Token);
        Assert.Single(bed.Sender.Syncs);
        await AdvanceUntilAsync(bed, () => bed.Sender.Syncs.Count() == 2);
        await AdvanceUntilAsync(bed, () => bed.Sender.Syncs.Count() == 3);
        await bed.CatchUp.WaitAsync();

        Assert.Equal(3, bed.Sender.Syncs.Count());
    }

    [Theory]
    [InlineData(RescanStages.QuickSyncJob, WorkItemState.Queued, 1)]
    [InlineData(RescanStages.RescanJob, WorkItemState.Queued, 1)]
    [InlineData(RescanStages.QuickSyncJob, WorkItemState.Running, 2)]
    [InlineData(RescanStages.QuickSyncJob, WorkItemState.Completed, 2)]
    public async Task Reconnected_BusyBehindALibraryWideSync_GivesUpOnlyIfItsListingIsToCome(string type, WorkItemState listing, int syncs)
    {
        // As when a library opens: a sync yet to list the folders lists the one that came back, so the catch-up doesn't
        // (MFC-1049); once the listing ran, the folder may not be in it, and the catch-up retries.
        using var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        var job = new ProcessingJob { Id = Id<ProcessingJob>.New(), Type = type, Description = "Sync", State = ProcessingJobState.Running, CreatedAt = Now };
        bed.Library.JobList.Unfinished.Add(job);
        bed.Library.JobList.Work.Add(WorkItem.Create(job.Id, null, RescanStages.Reconcile, ResourcePool.Io, 0, null, Now) with { State = listing });
        bed.Sender.Outcomes.Enqueue(StartRescanOutcome.Busy);

        await bed.Handler.Handle(new FoldersReconnected([folder.Id]), Token);
        await WaitUntilAsync(() => bed.Sender.Syncs.Count() == 1);
        if (syncs > 1)
        {
            await AdvanceUntilAsync(bed, () => bed.Sender.Syncs.Count() == syncs);
        }

        await bed.CatchUp.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10), Token);
        bed.Clock.Advance(ReconnectCatchUp.BusyRetry);
        await Task.Delay(50, Token);
        Assert.Equal(syncs, bed.Sender.Syncs.Count());
    }

    [Fact]
    public async Task Reconnected_WhileTheFolderIsBusy_StopsRetrying_WhenTheFolderGoesOffline()
    {
        using var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        bed.Sender.Then = StartRescanOutcome.Busy;

        await bed.Handler.Handle(new FoldersReconnected([folder.Id]), Token);
        await WaitUntilAsync(() => bed.Sender.Syncs.Count() == 1);
        folder.MarkOffline(Now);
        bed.Library.FolderList.Seed(folder);

        // The retry's timer isn't there yet when the first sync is seen, so one advance may come too early.
        await AdvanceUntilAsync(bed, () => bed.CatchUp.WaitAsync().IsCompleted);

        Assert.Single(bed.Sender.Syncs);
    }

    [Fact]
    public async Task Reconnected_WhileTheFolderIsBusy_StopsRetrying_WhenTheLibraryCloses()
    {
        using var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        bed.Sender.Then = StartRescanOutcome.Busy;

        await bed.Handler.Handle(new FoldersReconnected([folder.Id]), Token);
        await WaitUntilAsync(() => bed.Sender.Syncs.Count() == 1);
        await bed.Session.SetCurrentAsync(null);

        // The retry's timer isn't there yet when the first sync is seen, so one advance may come too early.
        await AdvanceUntilAsync(bed, () => bed.CatchUp.WaitAsync().IsCompleted);

        Assert.Single(bed.Sender.Syncs);
    }

    [Theory]
    [InlineData(StartRescanOutcome.NothingToScan)]
    [InlineData(StartRescanOutcome.Unavailable)]
    public async Task Reconnected_SyncRefusedForGood_IsNotRetried(StartRescanOutcome outcome)
    {
        using var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        bed.Sender.Then = outcome;

        await bed.ReconnectAsync(folder);
        bed.Clock.Advance(ReconnectCatchUp.BusyRetry * 2);
        await bed.CatchUp.WaitAsync();

        Assert.Single(bed.Sender.Syncs);
    }

    [Fact]
    public async Task Reconnected_SyncThatThrows_IsLoggedNotThrown()
    {
        using var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        bed.Sender.OnSync = () => throw new InvalidOperationException("The library closed.");

        await bed.ReconnectAsync(folder);

        Assert.Single(bed.Sender.Syncs);

        // The folder isn't left marked as waiting: the next reconnect asks again.
        bed.Sender.OnSync = null;
        await bed.ReconnectAsync(folder);
        Assert.Equal(2, bed.Sender.Syncs.Count());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < end)
        {
            await Task.Delay(10, Token);
        }

        Assert.True(condition());
    }

    /// <summary>
    /// Moves the clock on by the retry wait until the condition holds; a retry's wait starts on a thread-pool thread.
    /// </summary>
    private static async Task AdvanceUntilAsync(Bed bed, Func<bool> condition)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < end)
        {
            bed.Clock.Advance(ReconnectCatchUp.BusyRetry);
            await Task.Delay(20, Token);
        }

        Assert.True(condition());
    }

    /// <summary>
    /// A catch-up on a fake library, with a sender that records the syncs.
    /// </summary>
    private sealed class Bed : IDisposable
    {
        private Bed(bool watched)
        {
            Library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "reconnect"), "reconnect");
            var locations = new FolderLocations(NullLogger<FolderLocations>.Instance, Probe);
            Manager = new FolderWatchManager(Session, new InMemoryMediaFileSource(), new FakePublisher(), Sender, Settings, new NoFormats(), Clock, NullLogger<FolderWatchManager>.Instance, watched ? new QuietWatcherFactory() : null, locations: locations);
            CatchUp = new ReconnectCatchUp(Session, Manager, Sender, Clock, NullLogger<ReconnectCatchUp>.Instance, locations: locations);
            Handler = new FoldersReconnectedHandler(CatchUp);
        }

        public FakeLibrarySession Session { get; } = new();

        public FakeFolderLocationProbe Probe { get; } = new();

        public FakeOpenLibrary Library { get; }

        public ManualClock Clock { get; } = new();

        public InMemorySettingsStore Settings { get; } = new();

        public SyncSender Sender { get; } = new();

        public FolderWatchManager Manager { get; }

        public ReconnectCatchUp CatchUp { get; }

        public FoldersReconnectedHandler Handler { get; }

        public bool WatchedAtSync { get; set; }

        public static async Task<Bed> CreateAsync(bool watched = false)
        {
            var bed = new Bed(watched);
            await bed.Session.SetCurrentAsync(bed.Library);
            bed.Manager.Start();
            return bed;
        }

        public async Task<IndexedFolder> AddFolderAsync(string path)
        {
            var folder = IndexedFolder.Create(Library.Library.Id, path, path, Now);
            await Library.FolderList.AddAsync(folder, Token);
            return folder;
        }

        public async Task ReconnectAsync(params IndexedFolder[] folders)
        {
            await Handler.Handle(new FoldersReconnected([.. folders.Select(folder => folder.Id)]), Token);
            await CatchUp.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10), Token);
        }

        public void Dispose()
        {
            CatchUp.Dispose();
            Manager.Dispose();
        }
    }

    /// <summary>
    /// Records the syncs and answers them with the queued outcomes, then with <see cref="Then"/>.
    /// </summary>
    private sealed class SyncSender : FakeSender
    {
        private readonly ConcurrentQueue<StartRescanCommand> syncs = new();

        public ConcurrentQueue<StartRescanOutcome> Outcomes { get; } = new();

        public StartRescanOutcome Then { get; set; } = StartRescanOutcome.Started;

        public Action? OnSync { get; set; }

        public IEnumerable<StartRescanCommand> Syncs => syncs;

        protected override object? Handle(object request)
        {
            if (request is not StartRescanCommand sync)
            {
                return base.Handle(request);
            }

            syncs.Enqueue(sync);
            OnSync?.Invoke();
            return new StartRescanResult(null, Outcomes.TryDequeue(out var next) ? next : Then);
        }
    }

    /// <summary>
    /// Creates watchers that raise nothing.
    /// </summary>
    private sealed class QuietWatcherFactory : IFolderWatcherFactory
    {
        public IFolderWatcher Watch(FolderHandle folder) => new Watcher();

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

    /// <summary>
    /// Knows no media formats; watching isn't exercised here.
    /// </summary>
    private sealed class NoFormats : IMediaFormatRegistry
    {
        public IReadOnlyList<IMediaFormatHandler> Handlers => [];

        public bool IsCandidate(string fileName) => false;

        public IMediaFormatHandler? FindByFormatId(string formatId) => null;

        public Task<MediaFormatDetection> DetectAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Detection isn't needed here.");
    }
}
