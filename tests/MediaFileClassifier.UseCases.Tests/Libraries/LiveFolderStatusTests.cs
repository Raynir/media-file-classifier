using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Tests for live folder status (E14-S07-T04, DNEW-4): volume events trigger one debounced check of every folder, the
/// Offline folders alone are polled every 30 s, checks don't overlap, a volume check cancels a stuck poll, and a status
/// change stored by any writer refreshes the results.
/// </summary>
public sealed class LiveFolderStatusTests
{
    private const string Drive = @"E:\Photos";
    private const string Card = @"F:\Card";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VolumeEvents_AreDebounced_IntoOneCheckOfEveryFolder()
    {
        var (monitor, clock, volumes, sender) = Create();
        monitor.Start();

        volumes.Raise();
        clock.Advance(TimeSpan.FromMilliseconds(600));
        volumes.Raise();
        clock.Advance(TimeSpan.FromMilliseconds(600));
        Assert.Empty(sender.Commands);

        clock.Advance(TimeSpan.FromMilliseconds(400));

        var command = await sender.NextAsync();
        Assert.Equal(new CheckFolderAvailabilityCommand(null, OfflineOnly: false), command);
        Assert.Single(sender.Commands);
    }

    [Fact]
    public async Task Poll_ChecksTheOfflineFoldersOnly_EveryThirtySeconds()
    {
        var (monitor, clock, _, sender) = Create();
        monitor.Start();

        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Empty(sender.Commands);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new CheckFolderAvailabilityCommand(null, OfflineOnly: true), await sender.NextAsync());

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(new CheckFolderAvailabilityCommand(null, OfflineOnly: true), await sender.NextAsync());
    }

    [Fact]
    public async Task Poll_IsSkipped_WhileACheckIsRunning_AndAVolumeCheckWaitsForIt()
    {
        var (monitor, clock, volumes, sender) = Create();
        var hold = new TaskCompletionSource();
        sender.Hold = hold;
        monitor.Start();

        volumes.Raise();
        clock.Advance(FolderAvailabilityMonitor.Debounce);
        await sender.NextAsync();

        // The poll finds the check running, and a second volume check queues behind it.
        clock.Advance(FolderAvailabilityMonitor.PollInterval);
        volumes.Raise();
        clock.Advance(FolderAvailabilityMonitor.Debounce);
        Assert.Single(sender.Commands);

        hold.SetResult();
        Assert.Equal(new CheckFolderAvailabilityCommand(null, OfflineOnly: false), await sender.NextAsync());
        Assert.Equal(2, sender.Commands.Count);
    }

    [Fact]
    public async Task VolumeCheck_CancelsARunningPoll_InsteadOfWaitingForIt()
    {
        var (monitor, clock, volumes, sender) = Create();

        // Never set: only the poll's cancellation ends it, like a probe of an unreachable share.
        sender.Hold = new TaskCompletionSource();
        monitor.Start();

        clock.Advance(FolderAvailabilityMonitor.PollInterval);
        Assert.True((await sender.NextAsync()).OfflineOnly);

        volumes.Raise();
        clock.Advance(FolderAvailabilityMonitor.Debounce);

        Assert.False((await sender.NextAsync()).OfflineOnly);
        Assert.Equal(2, sender.Commands.Count);
    }

    [Fact]
    public async Task AFailedCheck_DoesntStopThePoll()
    {
        var (monitor, clock, _, sender) = Create();
        sender.Failure = new ObjectDisposedException("library");
        monitor.Start();

        clock.Advance(FolderAvailabilityMonitor.PollInterval);
        await sender.NextAsync();
        sender.Failure = null;
        clock.Advance(FolderAvailabilityMonitor.PollInterval);

        Assert.Equal(new CheckFolderAvailabilityCommand(null, OfflineOnly: true), await sender.NextAsync());
    }

    [Fact]
    public void Dispose_StopsListeningAndPolling()
    {
        var (monitor, clock, volumes, sender) = Create();
        monitor.Start();

        monitor.Dispose();
        volumes.Raise();
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(sender.Commands);
        Assert.Equal(0, volumes.Listeners);
    }

    [Fact]
    public async Task OfflineOnlyCheck_SkipsTheOnlineFolders_AndBringsBackAReconnectedOne()
    {
        var (session, library, files) = await CreateLibraryAsync();
        var drive = await AddFolderAsync(library, Drive, offline: true);
        await AddFolderAsync(library, Card, offline: false);
        var clock = new ManualClock();
        using var statusChanges = new FolderStatusChanges(session, clock);
        var raised = 0;
        statusChanges.Changed += (_, _) => raised++;
        var handler = Handler(session, files);

        // Still unplugged: nothing changes, and the Online card isn't even looked at.
        files.Unavailable.Add(Drive);
        files.Unavailable.Add(Card);
        Assert.Empty(await handler.Handle(new CheckFolderAvailabilityCommand(OfflineOnly: true), Token));
        clock.Advance(FolderStatusChanges.Quiet);
        Assert.Equal(0, raised);

        // Plugged back in.
        files.Unavailable.Remove(Drive);
        var changed = await handler.Handle(new CheckFolderAvailabilityCommand(OfflineOnly: true), Token);
        clock.Advance(FolderStatusChanges.Quiet);

        Assert.Same(drive, Assert.Single(changed));
        Assert.Equal(FolderStatus.Online, drive.Status);
        Assert.Equal(1, raised);
        Assert.Equal(1, library.FolderList.AvailabilityUpdates);
        Assert.Equal(1, library.FolderList.Updates);
    }

    [Fact]
    public async Task Check_WithAProbeThatHangs_EndsWhenCancelled()
    {
        var (session, library, files) = await CreateLibraryAsync();
        await AddFolderAsync(library, Drive, offline: true);
        files.Hanging.Add(Drive);
        using var cancel = new CancellationTokenSource();

        var check = Handler(session, files).Handle(new CheckFolderAvailabilityCommand(OfflineOnly: true), cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.WaitAsync(TimeSpan.FromSeconds(10), Token));
    }

    [Fact]
    public async Task AnyWriterThatChangesAStatus_RaisesTheChange_ButALabelOrRuleChangeDoesnt()
    {
        var (session, library, _) = await CreateLibraryAsync();
        var drive = await AddFolderAsync(library, Drive, offline: true);
        var clock = new ManualClock();
        using var statusChanges = new FolderStatusChanges(session, clock);
        var source = new LibraryItemsSource(new FakeSender(), session, TimeProvider.System, statusChanges);
        var raised = 0;
        source.Changed += (_, _) => raised++;

        // As a rescan or discovery does when it finds the folder again.
        drive.MarkOnline(DateTimeOffset.UnixEpoch);
        await library.Folders.UpdateAsync(drive, Token);
        clock.Advance(FolderStatusChanges.Quiet);
        Assert.Equal(1, raised);

        drive.ChangeVolumeLabel("Backup");
        await library.Folders.UpdateAvailabilityAsync(drive, Token);
        await library.Folders.UpdateAsync(drive, Token);
        clock.Advance(FolderStatusChanges.Quiet);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task AStatusChange_InALibraryNoLongerOpen_IsNotRaised()
    {
        var (session, oldLibrary, _) = await CreateLibraryAsync();
        var drive = await AddFolderAsync(oldLibrary, Drive, offline: true);
        var clock = new ManualClock();
        using var statusChanges = new FolderStatusChanges(session, clock);
        var raised = 0;
        statusChanges.Changed += (_, _) => raised++;

        // One change made just before the library closes, and one after.
        drive.MarkOffline(DateTimeOffset.UnixEpoch);
        await oldLibrary.Folders.UpdateAsync(drive, Token);
        await session.SetCurrentAsync(new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "y"), "y"));
        drive.MarkOnline(DateTimeOffset.UnixEpoch);
        await oldLibrary.Folders.UpdateAsync(drive, Token);
        clock.Advance(FolderStatusChanges.Quiet);

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task OneCheckThatFindsManyFoldersOffline_GivesOneResultsRefresh_AfterTheBurst()
    {
        var (session, library, files) = await CreateLibraryAsync();
        var hub = new List<string>();
        for (var i = 0; i < 15; i++)
        {
            var path = $@"G:\Hub\Folder{i}";
            hub.Add(path);
            await AddFolderAsync(library, path, offline: false);
            files.Unavailable.Add(path);
        }

        var clock = new ManualClock();
        using var statusChanges = new FolderStatusChanges(session, clock);
        var raised = 0;
        statusChanges.Changed += (_, _) => raised++;
        var source = new LibraryItemsSource(new FakeSender(), session, TimeProvider.System, statusChanges);
        var refreshed = 0;
        source.Changed += (_, _) => refreshed++;

        Assert.Equal(15, (await Handler(session, files).Handle(new CheckFolderAvailabilityCommand(), Token)).Count);
        clock.Advance(FolderStatusChanges.Quiet - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, raised);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal((1, 1), (raised, refreshed));

        // The hub is plugged back in: the next burst is reported once too.
        files.Unavailable.Clear();
        Assert.Equal(15, (await Handler(session, files).Handle(new CheckFolderAvailabilityCommand(), Token)).Count);
        clock.Advance(FolderStatusChanges.Quiet);
        Assert.Equal((2, 2), (raised, refreshed));
    }

    [Fact]
    public async Task EachChange_RestartsTheWait_SoAStaggeredBurstIsReportedOnceItsOver()
    {
        var (session, library, _) = await CreateLibraryAsync();
        var drive = await AddFolderAsync(library, Drive, offline: true);
        var card = await AddFolderAsync(library, Card, offline: true);
        var clock = new ManualClock();
        using var statusChanges = new FolderStatusChanges(session, clock);
        var raised = 0;
        statusChanges.Changed += (_, _) => raised++;
        var step = FolderStatusChanges.Quiet - TimeSpan.FromMilliseconds(50);

        drive.MarkOnline(DateTimeOffset.UnixEpoch);
        await library.Folders.UpdateAsync(drive, Token);
        clock.Advance(step);
        card.MarkOnline(DateTimeOffset.UnixEpoch);
        await library.Folders.UpdateAsync(card, Token);
        clock.Advance(step);
        Assert.Equal(0, raised);

        clock.Advance(FolderStatusChanges.Quiet);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task ADisposedWatcher_ReportsNoPendingChange()
    {
        var (session, library, _) = await CreateLibraryAsync();
        var drive = await AddFolderAsync(library, Drive, offline: true);
        var clock = new ManualClock();
        var statusChanges = new FolderStatusChanges(session, clock);
        var raised = 0;
        statusChanges.Changed += (_, _) => raised++;

        drive.MarkOnline(DateTimeOffset.UnixEpoch);
        await library.Folders.UpdateAsync(drive, Token);
        statusChanges.Dispose();
        clock.Advance(FolderStatusChanges.Quiet);

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task StatusChange_RaisesTheResultsChanged()
    {
        var (session, _, _) = await CreateLibraryAsync();
        using var statusChanges = new FolderStatusChanges();
        var source = new LibraryItemsSource(new FakeSender(), session, TimeProvider.System, statusChanges);
        var raised = 0;
        source.Changed += (_, _) => raised++;

        statusChanges.Raise();

        Assert.Equal(1, raised);
    }

    private static (FolderAvailabilityMonitor Monitor, ManualClock Clock, FakeVolumeWatcher Volumes, RecordingSender Sender) Create()
    {
        var clock = new ManualClock();
        var volumes = new FakeVolumeWatcher();
        var sender = new RecordingSender();
        return (new FolderAvailabilityMonitor(volumes, sender, clock, NullLogger<FolderAvailabilityMonitor>.Instance), clock, volumes, sender);
    }

    private static CheckFolderAvailabilityCommandHandler Handler(FakeLibrarySession session, FakeFolderSource files) =>
        new(session, files, TimeProvider.System, NullLogger<CheckFolderAvailabilityCommandHandler>.Instance);

    private static async Task<IndexedFolder> AddFolderAsync(FakeOpenLibrary library, string path, bool offline)
    {
        var folder = IndexedFolder.Create(library.Library.Id, path, path, DateTimeOffset.UnixEpoch);
        if (offline)
        {
            folder.MarkOffline(DateTimeOffset.UnixEpoch);
        }

        await library.Folders.AddAsync(folder, Token);
        return folder;
    }

    private static async Task<(FakeLibrarySession Session, FakeOpenLibrary Library, FakeFolderSource Files)> CreateLibraryAsync()
    {
        var session = new FakeLibrarySession();
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "x"), "x");
        await session.SetCurrentAsync(library);
        return (session, library, new FakeFolderSource());
    }

    private sealed class FakeVolumeWatcher : IVolumeWatcher
    {
        private EventHandler? handlers;

        public event EventHandler? VolumesChanged
        {
            add
            {
                handlers += value;
                Listeners++;
            }

            remove
            {
                handlers -= value;
                Listeners--;
            }
        }

        public int Listeners { get; private set; }

        public void Raise() => handlers?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records the availability checks, and lets a test wait for the next one or hold a check open.
    /// </summary>
    private sealed class RecordingSender : FakeSender
    {
        private readonly Lock syncRoot = new();
        private readonly Queue<TaskCompletionSource<CheckFolderAvailabilityCommand>> waiting = new();
        private readonly Queue<CheckFolderAvailabilityCommand> unseen = new();

        public List<CheckFolderAvailabilityCommand> Commands { get; } = [];

        public TaskCompletionSource? Hold { get; set; }

        public Exception? Failure { get; set; }

        public override async Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var command = Assert.IsType<CheckFolderAvailabilityCommand>(request);
            var failure = Failure;
            var hold = Hold;
            lock (syncRoot)
            {
                Commands.Add(command);
                if (waiting.TryDequeue(out var waiter))
                {
                    waiter.SetResult(command);
                }
                else
                {
                    unseen.Enqueue(command);
                }
            }

            if (hold is not null)
            {
                Hold = null;
                await hold.Task.WaitAsync(cancellationToken);
            }

            if (failure is not null)
            {
                throw failure;
            }

            return (TResponse)(object)Array.Empty<IndexedFolder>();
        }

        public Task<CheckFolderAvailabilityCommand> NextAsync()
        {
            lock (syncRoot)
            {
                if (unseen.TryDequeue(out var command))
                {
                    return Task.FromResult(command);
                }

                var waiter = new TaskCompletionSource<CheckFolderAvailabilityCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
                waiting.Enqueue(waiter);
                return waiter.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            }
        }
    }
}
