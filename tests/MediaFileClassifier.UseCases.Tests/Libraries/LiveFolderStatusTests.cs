using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Tests for live folder status (E14-S07-T04, DNEW-4): volume events trigger one debounced check of every folder, the
/// Offline folders alone are polled every 30 s, checks don't overlap, and a status change refreshes the results.
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
        var statusChanges = new FolderStatusChanges();
        var raised = 0;
        statusChanges.Changed += (_, _) => raised++;
        var handler = Handler(session, files, statusChanges);

        // Still unplugged: nothing changes, and the Online card isn't even looked at.
        files.Unavailable.Add(Drive);
        files.Unavailable.Add(Card);
        Assert.Empty(await handler.Handle(new CheckFolderAvailabilityCommand(OfflineOnly: true), Token));
        Assert.Equal(0, raised);

        // Plugged back in.
        files.Unavailable.Remove(Drive);
        var changed = await handler.Handle(new CheckFolderAvailabilityCommand(OfflineOnly: true), Token);

        Assert.Same(drive, Assert.Single(changed));
        Assert.Equal(FolderStatus.Online, drive.Status);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task StatusChange_RaisesTheResultsChanged()
    {
        var (session, _, _) = await CreateLibraryAsync();
        var statusChanges = new FolderStatusChanges();
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

    private static CheckFolderAvailabilityCommandHandler Handler(FakeLibrarySession session, FakeFolderSource files, FolderStatusChanges statusChanges) =>
        new(session, files, TimeProvider.System, NullLogger<CheckFolderAvailabilityCommandHandler>.Instance, statusChanges: statusChanges);

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

    /// <summary>
    /// A clock whose one-shot and periodic timers fire only when the test advances it.
    /// </summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> timers = [];
        private TimeSpan now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan time)
        {
            var end = now + time;
            while (timers.Where(timer => timer.DueAt <= end).MinBy(timer => timer.DueAt) is { } next)
            {
                now = next.DueAt!.Value;
                next.Fire();
            }

            now = end;
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan period = Timeout.InfiniteTimeSpan;

            public TimeSpan? DueAt { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                this.period = period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime;
                return true;
            }

            public void Fire()
            {
                DueAt = period == Timeout.InfiniteTimeSpan ? null : clock.now + period;
                callback(state);
            }

            public void Dispose() => DueAt = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
