using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Platform;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediaFileClassifier.UseCases.Tests.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Monitoring;

/// <summary>
/// Tests for <see cref="FolderPoller"/> (E04-S05-T02): the Online folders in Poll mode get a quick sync every poll
/// interval, and the polls follow the folders, the library and the interval.
/// </summary>
public sealed class FolderPollerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(MonitoringSettings.DefaultPollIntervalMinutes);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_PollsThePollFolders_AndTheNetworkFoldersInAuto_Only()
    {
        using var bed = new Bed();
        bed.Probe.Network(@"\\nas\photos");
        var share = await bed.AddFolderAsync(@"\\nas\photos");
        var polled = await bed.AddFolderAsync(@"E:\Polled", MonitoringMode.Poll);
        await bed.AddFolderAsync(@"E:\Local");
        await bed.AddFolderAsync(@"\\nas\manual", MonitoringMode.Manual);
        bed.Probe.Network(@"\\nas\watched");
        await bed.AddFolderAsync(@"\\nas\watched", MonitoringMode.Watch);
        var offline = await bed.AddFolderAsync(@"E:\Offline", MonitoringMode.Poll);
        offline.MarkOffline(Now);
        bed.Library.FolderList.Seed(offline);

        await bed.StartAsync();

        Assert.Equal(new HashSet<Id<IndexedFolder>> { share.Id, polled.Id }, bed.Poller.PolledFolders.ToHashSet());
        Assert.Equal(Now + Interval, bed.Poller.NextPollOf(share.Id));
    }

    [Fact]
    public async Task Interval_Elapsing_QueuesAQuickSyncOfEachPolledFolder_AndAgainAfterTheNextInterval()
    {
        using var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Polled", MonitoringMode.Poll);
        await bed.StartAsync();

        await bed.AdvanceAsync(Interval - TimeSpan.FromSeconds(1));
        Assert.Empty(bed.Sender.Polls);

        await bed.AdvanceAsync(TimeSpan.FromSeconds(1));
        Assert.Equal([new StartRescanCommand(folder.Id, Quick: true)], bed.Sender.Polls);

        await bed.AdvanceAsync(Interval);
        Assert.Equal(2, bed.Sender.Polls.Count());
        Assert.Equal(Now + (3 * Interval), bed.Poller.NextPollOf(folder.Id));
    }

    [Fact]
    public async Task Poll_RefusedAsBusy_WaitsForTheNextInterval()
    {
        using var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Polled", MonitoringMode.Poll);
        bed.Sender.Then = StartRescanOutcome.Busy;
        await bed.StartAsync();

        await bed.AdvanceAsync(Interval);
        await bed.AdvanceAsync(Interval - TimeSpan.FromSeconds(1));

        Assert.Single(bed.Sender.Polls);
        Assert.Equal(Now + (2 * Interval), bed.Poller.NextPollOf(folder.Id));
    }

    [Fact]
    public async Task Poll_ThatFails_DoesNotStopTheOtherFoldersPolls()
    {
        using var bed = new Bed();
        var first = await bed.AddFolderAsync(@"E:\First", MonitoringMode.Poll);
        var second = await bed.AddFolderAsync(@"E:\Second", MonitoringMode.Poll);
        bed.Sender.FailFor.Add(first.Id);
        await bed.StartAsync();

        await bed.AdvanceAsync(Interval);

        Assert.Equal(new HashSet<Id<IndexedFolder>> { first.Id, second.Id }, bed.Sender.Polls.Select(poll => poll.FolderId!.Value).ToHashSet());
        Assert.Equal(Now + (2 * Interval), bed.Poller.NextPollOf(first.Id));
    }

    [Fact]
    public async Task Folder_ModeChangedOrOffline_StopsAndStartsItsPolls_AndPublishesTheChange()
    {
        using var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Share", MonitoringMode.Poll);
        await bed.StartAsync();
        var published = bed.Publisher.Of<FolderMonitoringChanged>().Count;

        folder.ChangeMonitoringMode(MonitoringMode.Manual);
        await bed.Library.FolderList.UpdateAsync(folder, Token);
        await bed.Poller.RefreshAsync();
        Assert.Empty(bed.Poller.PolledFolders);

        folder.ChangeMonitoringMode(MonitoringMode.Poll);
        await bed.Library.FolderList.UpdateAsync(folder, Token);
        await bed.Poller.RefreshAsync();
        Assert.Equal([folder.Id], bed.Poller.PolledFolders);

        folder.MarkOffline(Now);
        await bed.Library.FolderList.UpdateAvailabilityAsync(folder, Token);
        await bed.Poller.RefreshAsync();
        Assert.Empty(bed.Poller.PolledFolders);

        await bed.AdvanceAsync(2 * Interval);
        Assert.Empty(bed.Sender.Polls);
        await WaitForAsync(() => bed.Publisher.Of<FolderMonitoringChanged>().Count >= published + 3);
    }

    [Fact]
    public async Task MonitoringTurnedOff_StopsThePolls_AndTurnedOnPollsAgain()
    {
        using var bed = new Bed();
        bed.Settings.IsLibraryOpen = true;
        var folder = await bed.AddFolderAsync(@"E:\Polled", MonitoringMode.Poll);
        await bed.StartAsync();
        var published = bed.Publisher.Of<FolderMonitoringChanged>().Count;

        bed.Settings.SetValue(KnownSettings.MonitoringEnabled, false);
        await bed.Poller.RefreshAsync();
        Assert.Empty(bed.Poller.PolledFolders);
        await bed.AdvanceAsync(2 * Interval);
        Assert.Empty(bed.Sender.Polls);
        await WaitForAsync(() => bed.Publisher.Of<FolderMonitoringChanged>().Count > published);

        bed.Settings.SetValue(KnownSettings.MonitoringEnabled, true);
        await bed.Poller.RefreshAsync();
        Assert.Equal([folder.Id], bed.Poller.PolledFolders);
        await bed.AdvanceAsync(Interval);
        Assert.Single(bed.Sender.Polls);
    }

    [Fact]
    public async Task Poll_DueWhileMonitoringIsPaused_WaitsAndRunsOnceOnResume()
    {
        using var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Polled", MonitoringMode.Poll);
        await bed.StartAsync();

        bed.Pause.Pause();
        bed.Clock.Advance(Interval);
        bed.Clock.Advance(Interval);
        bed.Clock.Advance(Interval);
        Assert.False(bed.Poller.WaitForPollsAsync().IsCompleted);
        Assert.Empty(bed.Sender.Polls);

        bed.Pause.Resume();
        await bed.Poller.WaitForPollsAsync();
        Assert.Equal([new StartRescanCommand(folder.Id, Quick: true)], bed.Sender.Polls);
        Assert.Equal(Now + (4 * Interval), bed.Poller.NextPollOf(folder.Id));
    }

    [Fact]
    public async Task Pause_DuringARoundOfPolls_HoldsTheRestOfTheRound_UntilResume()
    {
        using var bed = new Bed();
        await bed.AddFolderAsync(@"E:\First", MonitoringMode.Poll);
        await bed.AddFolderAsync(@"E:\Second", MonitoringMode.Poll);
        await bed.StartAsync();
        bed.Sender.OnPoll = () => bed.Pause.Pause();

        bed.Clock.Advance(Interval);
        await WaitForAsync(() => bed.Sender.Polls.Any());
        Assert.False(bed.Poller.WaitForPollsAsync().IsCompleted);
        Assert.Single(bed.Sender.Polls);

        bed.Sender.OnPoll = null;
        bed.Pause.Resume();
        await bed.Poller.WaitForPollsAsync();
        Assert.Equal(2, bed.Sender.Polls.Count());
    }

    [Fact]
    public async Task Round_OfPolls_PublishesTheChange_SoTheNextPollShownMoves()
    {
        using var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Polled", MonitoringMode.Poll);
        await bed.StartAsync();
        var published = bed.Publisher.Of<FolderMonitoringChanged>().Count;

        await bed.AdvanceAsync(Interval);

        await WaitForAsync(() => bed.Publisher.Of<FolderMonitoringChanged>().Count > published);
    }

    [Fact]
    public async Task Library_Closing_StopsThePolls()
    {
        using var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Share", MonitoringMode.Poll);
        await bed.StartAsync();

        await bed.Session.SetCurrentAsync(null);
        await bed.Poller.RefreshAsync();
        await bed.AdvanceAsync(2 * Interval);

        Assert.Empty(bed.Poller.PolledFolders);
        Assert.Empty(bed.Sender.Polls);
    }

    [Fact]
    public async Task Interval_Shortened_AppliesAtOnce_AndLengthened_FromTheNextPoll()
    {
        using var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Share", MonitoringMode.Poll);
        await bed.StartAsync();

        bed.Settings.SetValue(MonitoringSettings.PollIntervalMinutes, 5);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), bed.Poller.NextPollOf(folder.Id));
        await bed.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.Single(bed.Sender.Polls);

        bed.Settings.SetValue(MonitoringSettings.PollIntervalMinutes, 60);
        Assert.Equal(Now + TimeSpan.FromMinutes(10), bed.Poller.NextPollOf(folder.Id));
        await bed.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.Equal(2, bed.Sender.Polls.Count());
        Assert.Equal(Now + TimeSpan.FromMinutes(70), bed.Poller.NextPollOf(folder.Id));
    }

    [Fact]
    public async Task Dispose_StopsThePolls()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Share", MonitoringMode.Poll);
        await bed.StartAsync();

        bed.Dispose();
        await bed.AdvanceAsync(2 * Interval);

        Assert.Empty(bed.Sender.Polls);
        Assert.False(bed.Poller.IsPolling);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var tries = 0; !condition(); tries++)
        {
            Assert.True(tries < 500, "The condition wasn't met in time.");
            await Task.Delay(10, Token);
        }
    }

    private sealed class Bed : IDisposable
    {
        public Bed()
        {
            Library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "polling"), "polling");
            Poller = new FolderPoller(
                Session,
                new FolderLocations(NullLogger<FolderLocations>.Instance, Probe),
                Sender,
                Publisher,
                Settings,
                Clock,
                NullLogger<FolderPoller>.Instance,
                Pause);
        }

        public MonitoringPause Pause { get; } = new();

        public FakeLibrarySession Session { get; } = new();

        public ManualClock Clock { get; } = new();

        public FakeFolderLocationProbe Probe { get; } = new();

        public PollSender Sender { get; } = new();

        public FakePublisher Publisher { get; } = new();

        public InMemorySettingsStore Settings { get; } = new();

        public FakeOpenLibrary Library { get; }

        public FolderPoller Poller { get; }

        public async Task<IndexedFolder> AddFolderAsync(string path, MonitoringMode mode = MonitoringMode.Auto)
        {
            var folder = IndexedFolder.Create(Library.Library.Id, path, path, Now);
            folder.ChangeMonitoringMode(mode);
            await Library.FolderList.AddAsync(folder, Token);
            return folder;
        }

        public async Task StartAsync()
        {
            await Session.SetCurrentAsync(Library);
            Poller.Start();
            await Poller.RefreshAsync();
        }

        public async Task AdvanceAsync(TimeSpan time)
        {
            Clock.Advance(time);
            await Poller.WaitForPollsAsync();
        }

        public void Dispose() => Poller.Dispose();
    }

    private sealed class PollSender : FakeSender
    {
        private readonly ConcurrentQueue<StartRescanCommand> polls = new();

        public StartRescanOutcome Then { get; set; } = StartRescanOutcome.Started;

        public HashSet<Id<IndexedFolder>> FailFor { get; } = [];

        public Action? OnPoll { get; set; }

        public IEnumerable<StartRescanCommand> Polls => polls;

        protected override object? Handle(object request)
        {
            if (request is not StartRescanCommand rescan)
            {
                throw new NotSupportedException(request.GetType().Name);
            }

            polls.Enqueue(rescan);
            OnPoll?.Invoke();
            if (rescan.FolderId is { } folderId && FailFor.Contains(folderId))
            {
                throw new InvalidOperationException("The library closed.");
            }

            return new StartRescanResult(null, Then);
        }
    }
}
