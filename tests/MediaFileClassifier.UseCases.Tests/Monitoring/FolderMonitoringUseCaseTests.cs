using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Platform;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Monitoring.ChangeMonitoringMode;
using MediaFileClassifier.UseCases.Monitoring.GetFolderMonitoring;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediaFileClassifier.UseCases.Tests.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Monitoring;

/// <summary>
/// Tests for <see cref="ChangeMonitoringModeCommand"/> and <see cref="GetFolderMonitoringQuery"/> (E04-S05-T01): the API
/// the folder settings bind to.
/// </summary>
public sealed class FolderMonitoringUseCaseTests
{
    private const string Share = @"\\nas\photos";
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChangeMode_StoresTheMode_AndAgainIsUnchanged()
    {
        using var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");

        var changed = await bed.ChangeAsync(new ChangeMonitoringModeCommand(folder.Id, MonitoringMode.Poll));
        var again = await bed.ChangeAsync(new ChangeMonitoringModeCommand(folder.Id, MonitoringMode.Poll));

        Assert.Equal((ChangeMonitoringModeResult.Changed, ChangeMonitoringModeResult.Unchanged), (changed, again));
        Assert.Equal(MonitoringMode.Poll, (await bed.Library.FolderList.GetAsync(folder.Id, Token))!.MonitoringMode);
        Assert.Empty(bed.Sender.Rescans);
    }

    [Fact]
    public async Task ChangeMode_OutOfManual_QueuesACatchUpSync_ButNotIntoManual()
    {
        using var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos", MonitoringMode.Manual);

        await bed.ChangeAsync(new ChangeMonitoringModeCommand(folder.Id, MonitoringMode.Auto));
        await bed.ChangeAsync(new ChangeMonitoringModeCommand(folder.Id, MonitoringMode.Manual));

        Assert.Equal([new StartRescanCommand(folder.Id, Quick: true)], bed.Sender.Rescans);
    }

    [Fact]
    public async Task ChangeMode_OfAnUnknownFolder_OrWithNoLibrary_IsNotFound()
    {
        using var bed = new Bed();

        var unknown = await bed.ChangeAsync(new ChangeMonitoringModeCommand(Id<IndexedFolder>.New(), MonitoringMode.Poll));
        await bed.Session.SetCurrentAsync(null);
        var closed = await bed.ChangeAsync(new ChangeMonitoringModeCommand(Id<IndexedFolder>.New(), MonitoringMode.Poll));

        Assert.Equal((ChangeMonitoringModeResult.NotFound, ChangeMonitoringModeResult.NotFound), (unknown, closed));
    }

    [Fact]
    public async Task ChangeMode_ToAnUndefinedMode_Throws()
    {
        using var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => bed.ChangeAsync(new ChangeMonitoringModeCommand(folder.Id, (MonitoringMode)9)));
    }

    [Fact]
    public async Task Query_ReportsEachFoldersModeStateAndLocation()
    {
        using var bed = new Bed();
        bed.Probe.Network(Share);
        bed.Probe.Locations[@"C:\Users\me\OneDrive"] = new FolderLocation(false, "OneDrive");
        var watched = await bed.AddFolderAsync(@"C:\Users\me\OneDrive");
        var share = await bed.AddFolderAsync(Share);
        var manual = await bed.AddFolderAsync(@"E:\Manual", MonitoringMode.Manual);
        var offline = await bed.AddFolderAsync(@"F:\Offline");
        offline.MarkOffline(Now);
        var failing = await bed.AddFolderAsync(@"G:\Failing");
        bed.Factory.Failing.Add(@"G:\Failing");
        await bed.StartAsync();

        var states = (await bed.QueryAsync()).ToDictionary(info => info.FolderId);

        Assert.Equal(new FolderMonitoringInfo(watched.Id, MonitoringMode.Auto, MonitoringMode.Watch, FolderMonitoringState.Watching, false, "OneDrive", null), states[watched.Id]);
        Assert.Equal(new FolderMonitoringInfo(share.Id, MonitoringMode.Auto, MonitoringMode.Poll, FolderMonitoringState.Polling, true, null, bed.Poller.NextPollOf(share.Id)), states[share.Id]);
        Assert.NotNull(states[share.Id].NextPoll);
        Assert.Equal(FolderMonitoringState.Manual, states[manual.Id].State);
        Assert.Equal(FolderMonitoringState.Offline, states[offline.Id].State);
        Assert.Equal(FolderMonitoringState.Error, states[failing.Id].State);
    }

    [Fact]
    public async Task Query_WhilePaused_IsPausedForWatchedAndPolledFolders_AndOffWhileMonitoringIsOff()
    {
        using var bed = new Bed();
        bed.Settings.IsLibraryOpen = true;
        bed.Probe.Network(Share);
        var watched = await bed.AddFolderAsync(@"E:\Watched");
        var share = await bed.AddFolderAsync(Share);
        var manual = await bed.AddFolderAsync(@"E:\Manual", MonitoringMode.Manual);
        var offline = await bed.AddFolderAsync(@"F:\Offline");
        offline.MarkOffline(Now);
        await bed.StartAsync();

        bed.Pause.Pause();
        var states = (await bed.QueryAsync()).ToDictionary(info => info.FolderId);
        Assert.Equal(FolderMonitoringState.Paused, states[watched.Id].State);
        Assert.Equal(FolderMonitoringState.Paused, states[share.Id].State);
        Assert.NotNull(states[share.Id].NextPoll);
        Assert.Equal(FolderMonitoringState.Manual, states[manual.Id].State);
        bed.Pause.Resume();

        bed.Settings.SetValue(KnownSettings.MonitoringEnabled, false);
        states = (await bed.QueryAsync()).ToDictionary(info => info.FolderId);
        Assert.Equal(FolderMonitoringState.Off, states[watched.Id].State);
        Assert.Equal(FolderMonitoringState.Off, states[share.Id].State);
        Assert.Null(states[share.Id].NextPoll);
        Assert.Equal(FolderMonitoringState.Off, states[manual.Id].State);
        Assert.Equal(FolderMonitoringState.Offline, states[offline.Id].State);
    }

    [Fact]
    public async Task Query_WithNoLibrary_IsEmpty()
    {
        using var bed = new Bed();
        await bed.Session.SetCurrentAsync(null);

        Assert.Empty(await bed.QueryAsync());
    }

    private sealed class Bed : IDisposable
    {
        public Bed()
        {
            Library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "modes"), "modes");
            Locations = new FolderLocations(NullLogger<FolderLocations>.Instance, Probe);
            Watchers = new FolderWatchManager(
                Session,
                new InMemoryMediaFileSource(),
                new FakePublisher(),
                Sender,
                Settings,
                new NoFormats(),
                Clock,
                NullLogger<FolderWatchManager>.Instance,
                Factory,
                locations: Locations);
            Poller = new FolderPoller(Session, Locations, Sender, new FakePublisher(), Settings, Clock, NullLogger<FolderPoller>.Instance);
            Session.SetCurrentAsync(Library).GetAwaiter().GetResult();
        }

        public FakeLibrarySession Session { get; } = new();

        public ManualClock Clock { get; } = new();

        public FakeFolderLocationProbe Probe { get; } = new();

        public RescanSender Sender { get; } = new();

        public InMemorySettingsStore Settings { get; } = new();

        public MonitoringPause Pause { get; } = new();

        public FailingWatcherFactory Factory { get; } = new();

        public FakeOpenLibrary Library { get; }

        public FolderLocations Locations { get; }

        public FolderWatchManager Watchers { get; }

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
            Watchers.Start();
            Poller.Start();
            await Watchers.RefreshAsync();
            await Poller.RefreshAsync();
        }

        public Task<ChangeMonitoringModeResult> ChangeAsync(ChangeMonitoringModeCommand command) =>
            new ChangeMonitoringModeCommandHandler(Session, Locations, Sender, NullLogger<ChangeMonitoringModeCommandHandler>.Instance).Handle(command, Token);

        public Task<IReadOnlyList<FolderMonitoringInfo>> QueryAsync() =>
            new GetFolderMonitoringQueryHandler(Session, Locations, Watchers, Poller, Settings, Pause).Handle(new GetFolderMonitoringQuery(), Token);

        public void Dispose()
        {
            Poller.Dispose();
            Watchers.Dispose();
        }
    }

    private sealed class RescanSender : FakeSender
    {
        private readonly List<StartRescanCommand> rescans = [];

        public IReadOnlyList<StartRescanCommand> Rescans
        {
            get
            {
                lock (rescans)
                {
                    return [.. rescans];
                }
            }
        }

        protected override object? Handle(object request)
        {
            switch (request)
            {
                case StartRescanCommand rescan:
                    lock (rescans)
                    {
                        rescans.Add(rescan);
                    }

                    return new StartRescanResult(null, StartRescanOutcome.Started);
                default:
                    // The availability check after a watcher can't start.
                    return null;
            }
        }
    }

    /// <summary>
    /// Watchers that do nothing, except for the folders that can't be watched.
    /// </summary>
    private sealed class FailingWatcherFactory : IFolderWatcherFactory
    {
        public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IFolderWatcher Watch(FolderHandle folder) =>
            Failing.Contains(folder.Id) ? throw new DirectoryNotFoundException(folder.Id) : new QuietWatcher();

        private sealed class QuietWatcher : IFolderWatcher
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

    private sealed class NoFormats : IMediaFormatRegistry
    {
        public IReadOnlyList<IMediaFormatHandler> Handlers => [];

        public bool IsCandidate(string fileName) => false;

        public IMediaFormatHandler? FindByFormatId(string formatId) => null;

        public Task<MediaFormatDetection> DetectAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Detection isn't needed.");
    }
}
