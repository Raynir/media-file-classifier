using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Platform;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Monitoring;

/// <summary>
/// Tests for <see cref="FolderWatchManager"/> (E04-S01-T01): watchers exist exactly for the Online folders of the open
/// library, and follow the library and its folders.
/// </summary>
public sealed class FolderWatchManagerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task Start_WatchesTheOnlineFolders_NotOfflineOrBeingRemoved()
    {
        var bed = new Bed();
        var online = await bed.AddFolderAsync(@"E:\Online");
        var offline = await bed.AddFolderAsync(@"E:\Offline");
        offline.MarkOffline(Now);
        bed.Library.FolderList.Seed(offline);
        var removing = await bed.AddFolderAsync(@"E:\Removing");
        bed.Library.JobList.Unfinished.Add(new ProcessingJob
        {
            Id = Id<ProcessingJob>.New(),
            Type = IndexedFolders.RemoveJob,
            Description = "Remove",
            FolderId = removing.Id,
            CreatedAt = Now,
        });

        await bed.StartAsync();

        Assert.Equal([online.Id], bed.Manager.WatchedFolders);
        Assert.Equal([@"E:\Online"], bed.Factory.Running);
    }

    [Fact]
    public async Task Start_WatchesOnlyTheFoldersInWatchMode_NotPollManualOrNetworkInAuto()
    {
        var bed = new Bed();
        bed.Probe.Network(@"\\nas\photos");
        bed.Probe.Network(@"\\nas\watched");
        var local = await bed.AddFolderAsync(@"E:\Local");
        await bed.AddFolderAsync(@"\\nas\photos");
        var watchedShare = await bed.AddFolderAsync(@"\\nas\watched");
        watchedShare.ChangeMonitoringMode(MonitoringMode.Watch);
        bed.Library.FolderList.Seed(watchedShare);
        var polled = await bed.AddFolderAsync(@"E:\Polled");
        polled.ChangeMonitoringMode(MonitoringMode.Poll);
        bed.Library.FolderList.Seed(polled);
        var manual = await bed.AddFolderAsync(@"E:\Manual");
        manual.ChangeMonitoringMode(MonitoringMode.Manual);
        bed.Library.FolderList.Seed(manual);

        await bed.StartAsync();

        Assert.Equal(new HashSet<Id<IndexedFolder>> { local.Id, watchedShare.Id }, bed.Manager.WatchedFolders.ToHashSet());
    }

    [Fact]
    public async Task Folder_ModeChanged_StopsAndStartsItsWatcher_AndPublishesTheChange()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();

        folder.ChangeMonitoringMode(MonitoringMode.Manual);
        await bed.Library.FolderList.UpdateAsync(folder, TestContext.Current.CancellationToken);
        await bed.Manager.RefreshAsync();
        Assert.Empty(bed.Manager.WatchedFolders);
        Assert.Empty(bed.Factory.Running);

        folder.ChangeMonitoringMode(MonitoringMode.Watch);
        await bed.Library.FolderList.UpdateAsync(folder, TestContext.Current.CancellationToken);
        await bed.Manager.RefreshAsync();
        Assert.Equal([folder.Id], bed.Manager.WatchedFolders);

        for (var tries = 0; bed.Publisher.Of<FolderMonitoringChanged>().Count < 3; tries++)
        {
            Assert.True(tries < 500, "The changes weren't published.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Pause_AndResume_PublishTheMonitoringChange()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();
        var before = bed.Publisher.Of<FolderMonitoringChanged>().Count;

        bed.Pause.Pause();
        await WaitForAsync(() => bed.Publisher.Of<FolderMonitoringChanged>().Count >= before + 1);
        bed.Pause.Resume();
        await WaitForAsync(() => bed.Publisher.Of<FolderMonitoringChanged>().Count >= before + 2);
    }

    private static async Task WaitForAsync(Func<bool> done)
    {
        for (var tries = 0; !done(); tries++)
        {
            Assert.True(tries < 500, "The change wasn't published.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Library_ClosingStopsEveryWatcher_AndOpeningWatchesItsFolders()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();

        await bed.Session.SetCurrentAsync(null);
        await bed.Manager.RefreshAsync();
        Assert.Empty(bed.Manager.WatchedFolders);
        Assert.Empty(bed.Factory.Running);

        await bed.Session.SetCurrentAsync(bed.Library);
        await bed.Manager.RefreshAsync();
        Assert.Equal([folder.Id], bed.Manager.WatchedFolders);
        Assert.Single(bed.Factory.Running);
    }

    [Fact]
    public async Task Folders_AddedAndDeleted_StartAndStopTheirWatchers()
    {
        var bed = new Bed();
        await bed.StartAsync();

        var folder = await bed.AddFolderAsync(@"E:\New");
        await bed.Manager.RefreshAsync();
        Assert.Equal([folder.Id], bed.Manager.WatchedFolders);

        await bed.Library.FolderList.DeleteAsync(folder.Id, TestContext.Current.CancellationToken);
        await bed.Manager.RefreshAsync();
        Assert.Empty(bed.Manager.WatchedFolders);
        Assert.Empty(bed.Factory.Running);
    }

    [Fact]
    public async Task Folder_GoingOfflineAndComingBack_StopsAndRestartsItsWatcher()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Drive");
        await bed.StartAsync();

        folder.MarkOffline(Now);
        await bed.Library.FolderList.UpdateAvailabilityAsync(folder, TestContext.Current.CancellationToken);
        await bed.Manager.RefreshAsync();
        Assert.Empty(bed.Manager.WatchedFolders);

        folder.MarkOnline(Now);
        await bed.Library.FolderList.UpdateAvailabilityAsync(folder, TestContext.Current.CancellationToken);
        await bed.Manager.RefreshAsync();
        Assert.Equal([folder.Id], bed.Manager.WatchedFolders);
        Assert.Equal(2, bed.Factory.Started);
    }

    [Fact]
    public async Task Folder_Relinked_IsWatchedAtItsNewRoot_AndARuleChangeKeepsTheWatcher()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Old");
        await bed.StartAsync();

        folder.ChangeRules(new FolderRules { ExcludePatterns = ["raw"] });
        await bed.Library.FolderList.UpdateAsync(folder, TestContext.Current.CancellationToken);
        await bed.Manager.RefreshAsync();
        Assert.Equal(1, bed.Factory.Started);

        folder.Relink(@"F:\New", @"F:\New", Now);
        await bed.Library.FolderList.UpdateAsync(folder, TestContext.Current.CancellationToken);
        await bed.Manager.RefreshAsync();
        Assert.Equal([@"F:\New"], bed.Factory.Running);
    }

    [Fact]
    public async Task Folder_ThatCantBeWatched_IsSkipped_AndTheOthersAreWatched()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Gone");
        var other = await bed.AddFolderAsync(@"E:\There");
        bed.Factory.Failing.Add(@"E:\Gone");

        await bed.StartAsync();

        Assert.Equal([other.Id], bed.Manager.WatchedFolders);
    }

    [Fact]
    public async Task State_OfAnOnlineFolder_IsWatching_Paused_OrError()
    {
        var bed = new Bed();
        var watched = await bed.AddFolderAsync(@"E:\Photos");
        var broken = await bed.AddFolderAsync(@"E:\Broken");
        bed.Factory.Failing.Add(@"E:\Broken");
        await bed.StartAsync();

        Assert.Equal(FolderMonitoringState.Watching, bed.Manager.GetState(watched.Id));
        Assert.Equal(FolderMonitoringState.Error, bed.Manager.GetState(broken.Id));

        bed.Pause.Pause();
        Assert.Equal(FolderMonitoringState.Paused, bed.Manager.GetState(watched.Id));

        bed.Pause.Resume();
        Assert.Equal(FolderMonitoringState.Watching, bed.Manager.GetState(watched.Id));
    }

    [Fact]
    public async Task State_OfAPollOrManualFolder_IsPolling_OrManual_PausedWhilePolledAndPaused_AndOffWhileMonitoringIsOff()
    {
        var bed = new Bed();
        bed.Settings.IsLibraryOpen = true;
        var polled = await bed.AddFolderAsync(@"E:\Polled", MonitoringMode.Poll);
        var manual = await bed.AddFolderAsync(@"E:\Manual", MonitoringMode.Manual);
        await bed.StartAsync();

        Assert.Empty(bed.Manager.WatchedFolders);
        Assert.Equal(FolderMonitoringState.Polling, bed.Manager.GetState(polled.Id));
        Assert.Equal(FolderMonitoringState.Manual, bed.Manager.GetState(manual.Id));

        bed.Pause.Pause();
        Assert.Equal(FolderMonitoringState.Paused, bed.Manager.GetState(polled.Id));
        Assert.Equal(FolderMonitoringState.Manual, bed.Manager.GetState(manual.Id));
        bed.Pause.Resume();

        bed.Settings.SetValue(KnownSettings.MonitoringEnabled, false);
        await bed.Manager.RefreshAsync();
        Assert.Equal(FolderMonitoringState.Off, bed.Manager.GetState(polled.Id));
        Assert.Equal(FolderMonitoringState.Off, bed.Manager.GetState(manual.Id));
    }

    [Fact]
    public async Task State_OfAnOfflineOrUnknownFolder_IsOff()
    {
        var bed = new Bed();
        var offline = await bed.AddFolderAsync(@"E:\Offline");
        offline.MarkOffline(Now);
        bed.Library.FolderList.Seed(offline);
        await bed.StartAsync();

        Assert.Equal(FolderMonitoringState.Off, bed.Manager.GetState(offline.Id));
        Assert.Equal(FolderMonitoringState.Off, bed.Manager.GetState(Id<IndexedFolder>.New()));
    }

    [Fact]
    public async Task MonitoringTurnedOff_StopsEveryWatcher_AndTurnedOnWatchesAgainWithAQuickSync()
    {
        var bed = new Bed();
        bed.Settings.IsLibraryOpen = true;
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();
        var states = 0;
        bed.Manager.StateChanged += (_, _) => states++;

        bed.Settings.SetValue(KnownSettings.MonitoringEnabled, false);
        await bed.Manager.RefreshAsync();
        Assert.Empty(bed.Manager.WatchedFolders);
        Assert.Equal(FolderMonitoringState.Off, bed.Manager.GetState(folder.Id));
        Assert.True(states > 0);

        bed.Settings.SetValue(KnownSettings.MonitoringEnabled, true);
        await bed.Manager.WaitForRecoveryAsync();
        Assert.Equal([folder.Id], bed.Manager.WatchedFolders);
        Assert.Contains(bed.Sender.Rescans, rescan => rescan.Quick && rescan.FolderId is null);
    }

    [Fact]
    public async Task Overflow_WhileMonitoringIsPaused_RescansOnlyOnceResumed()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();
        bed.Pause.Pause();

        bed.Factory.Fail(@"E:\Photos", overflow: true);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(bed.Sender.Rescans);

        bed.Pause.Resume();
        await bed.Manager.WaitForRecoveryAsync();
        Assert.Equal([folder.Id], bed.Sender.Rescans.Select(rescan => rescan.FolderId));
    }

    [Fact]
    public async Task Overflow_RescansTheFolder_AndKeepsWatchingIt()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        var other = await bed.AddFolderAsync(@"E:\Other");
        await bed.StartAsync();

        bed.Factory.Fail(@"E:\Photos", overflow: true);
        await bed.Manager.WaitForRecoveryAsync();

        Assert.Equal([folder.Id], bed.Sender.Rescans.Select(rescan => rescan.FolderId));
        Assert.Equal(2, bed.Manager.WatchedFolders.Count);
        Assert.Contains(folder.Id, bed.Manager.WatchedFolders);
        Assert.Contains(other.Id, bed.Manager.WatchedFolders);
        Assert.Equal(2, bed.Factory.Started);
    }

    [Fact]
    public async Task Overflow_WhileTheFolderIsBusy_IsRetriedEvery15Seconds_UntilARescanStarts()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();
        bed.Sender.Outcomes.Enqueue(StartRescanOutcome.Busy);
        bed.Sender.Outcomes.Enqueue(StartRescanOutcome.Busy);

        bed.Factory.Fail(@"E:\Photos", overflow: true);

        // A second overflow while one is waiting doesn't ask twice.
        bed.Factory.Fail(@"E:\Photos", overflow: true);
        Assert.Single(bed.Sender.Rescans);
        bed.Clock.Advance(FolderWatchManager.BusyRetry - TimeSpan.FromSeconds(1));
        Assert.Single(bed.Sender.Rescans);
        await AdvanceUntilAsync(bed, () => bed.Sender.Rescans.Count() == 2);
        await AdvanceUntilAsync(bed, () => bed.Sender.Rescans.Count() == 3);
        await bed.Manager.WaitForRecoveryAsync();

        Assert.All(bed.Sender.Rescans, rescan => Assert.Equal(folder.Id, rescan.FolderId));
        Assert.Equal(3, bed.Sender.Rescans.Count());
    }

    [Fact]
    public async Task Overflow_WhileTheFolderIsBusy_StopsRetrying_WhenTheFolderIsntWatchedAnymore()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();
        bed.Sender.Then = StartRescanOutcome.Busy;

        bed.Factory.Fail(@"E:\Photos", overflow: true);
        await bed.Library.FolderList.DeleteAsync(folder.Id, TestContext.Current.CancellationToken);
        await bed.Manager.RefreshAsync();
        bed.Clock.Advance(FolderWatchManager.BusyRetry);
        await bed.Manager.WaitForRecoveryAsync();

        Assert.Single(bed.Sender.Rescans);
    }

    [Theory]
    [InlineData(StartRescanOutcome.NothingToScan)]
    [InlineData(StartRescanOutcome.Unavailable)]
    public async Task Overflow_RescanRefusedForGood_IsNotRetried(StartRescanOutcome outcome)
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();
        bed.Sender.Then = outcome;

        bed.Factory.Fail(@"E:\Photos", overflow: true);
        bed.Clock.Advance(FolderWatchManager.BusyRetry * 2);
        await bed.Manager.WaitForRecoveryAsync();

        Assert.Single(bed.Sender.Rescans);
    }

    [Fact]
    public async Task Error_FolderStillOnline_ChecksIt_WatchesItAgain_AndRescansIt()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Share");
        await bed.StartAsync();

        bed.Factory.Fail(@"E:\Share", overflow: false);
        await bed.Manager.WaitForRecoveryAsync();

        var check = Assert.Single(bed.Sender.Requests.OfType<CheckFolderAvailabilityCommand>());
        Assert.Equal([folder.Id], check.FolderIds!);
        Assert.Equal(2, bed.Factory.Started);
        Assert.Equal([@"E:\Share"], bed.Factory.Running);
        Assert.Equal([folder.Id], bed.Sender.Rescans.Select(rescan => rescan.FolderId));
    }

    [Fact]
    public async Task Error_FolderNowOffline_StopsItsWatcher_WithoutARescan()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Share");
        await bed.StartAsync();
        bed.Sender.OnCheck = async _ =>
        {
            folder.MarkOffline(Now);
            await bed.Library.FolderList.UpdateAvailabilityAsync(folder, TestContext.Current.CancellationToken);
        };

        bed.Factory.Fail(@"E:\Share", overflow: false);
        await bed.Manager.WaitForRecoveryAsync();
        await bed.Manager.RefreshAsync();

        Assert.Empty(bed.Manager.WatchedFolders);
        Assert.Empty(bed.Factory.Running);
        Assert.Empty(bed.Sender.Rescans);
    }

    [Fact]
    public async Task Folder_ThatCantBeWatched_IsChecked()
    {
        var bed = new Bed();
        var gone = await bed.AddFolderAsync(@"E:\Gone");
        bed.Factory.Failing.Add(@"E:\Gone");

        await bed.StartAsync();
        await bed.Manager.WaitForRecoveryAsync();

        // Each refresh that can't watch it asks for a check.
        var checks = bed.Sender.Requests.OfType<CheckFolderAvailabilityCommand>().ToList();
        Assert.NotEmpty(checks);
        Assert.All(checks, check => Assert.Equal([gone.Id], check.FolderIds!));
        Assert.Empty(bed.Sender.Rescans);
    }

    [Fact]
    public async Task Error_AgainSoonAfterARestart_WaitsBeforeWatchingAgain_AndTheWaitDoubles()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Share");
        await bed.StartAsync();

        bed.Factory.Fail(@"E:\Share", overflow: false);
        await bed.Manager.WaitForRecoveryAsync();
        Assert.Equal(2, bed.Factory.Started);

        // Fails again at once: it isn't watched again for a minute, even by a refresh.
        bed.Factory.Fail(@"E:\Share", overflow: false);
        await bed.Manager.RefreshAsync();
        Assert.Equal(2, bed.Factory.Started);
        Assert.Empty(bed.Manager.WatchedFolders);
        await AdvanceUntilAsync(bed, FolderWatchManager.FirstFailureWait, () => bed.Factory.Started == 3);
        await bed.Manager.WaitForRecoveryAsync();

        // And again: two minutes this time.
        bed.Factory.Fail(@"E:\Share", overflow: false);
        bed.Clock.Advance(FolderWatchManager.FirstFailureWait);
        await bed.Manager.RefreshAsync();
        Assert.Equal(3, bed.Factory.Started);
        await AdvanceUntilAsync(bed, FolderWatchManager.FirstFailureWait, () => bed.Factory.Started == 4);
        await bed.Manager.WaitForRecoveryAsync();
        Assert.Equal(3, bed.Sender.Rescans.Count());
    }

    [Fact]
    public async Task Error_LongAfterTheLastOne_IsRecoveredAtOnce()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Share");
        await bed.StartAsync();
        bed.Factory.Fail(@"E:\Share", overflow: false);
        await bed.Manager.WaitForRecoveryAsync();

        bed.Clock.Advance(FolderWatchManager.MaxFailureWait);
        bed.Factory.Fail(@"E:\Share", overflow: false);
        await bed.Manager.WaitForRecoveryAsync();

        Assert.Equal(3, bed.Factory.Started);
    }

    [Fact]
    public async Task FolderDeletion_IsReported()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();

        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Deleted, "Trip 2020"));
        bed.Settle();

        var change = Assert.Single(bed.Published);
        Assert.Equal((FolderChangeKind.Deleted, "Trip 2020"), (change.Kind, change.RelativePath));
    }

    [Fact]
    public async Task Folder_MovedInOrRenamed_HasItsFilesFilteredLikeSingleFiles()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        folder.ChangeRules(new FolderRules { ExcludePatterns = ["raw"] });
        bed.Library.FolderList.Seed(folder);
        await bed.StartAsync();
        foreach (var path in new[] { "new/a.jpg", "new/notes.txt", "new/raw/b.jpg", "new/c.jpg.part", "2025/d.jpg", "2025/notes.txt" })
        {
            bed.Files.Write(path, 10, bed.Clock.GetUtcNow());
        }

        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Created, "new", IsDirectory: true));
        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Renamed, "2025", "2024", IsDirectory: true));
        bed.Settle();

        Assert.Equal(
            [(FolderChangeKind.Changed, "new/a.jpg", null), (FolderChangeKind.Renamed, "2025/d.jpg", "2024/d.jpg")],
            bed.Published.Select(change => (change.Kind, change.RelativePath, change.OldRelativePath)).OrderBy(change => change.Kind));
    }

    [Fact]
    public async Task Folder_RenamedTwiceAtOnce_IsOneRenameFromItsFirstName()
    {
        // MFC-1032: by the first event, Photos2 was renamed again, so the watcher can't tell it was a folder.
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Library");
        await bed.StartAsync();
        bed.Files.Write("Photos3/a.jpg", 10, bed.Clock.GetUtcNow());

        bed.Factory.Raise(@"E:\Library", new FolderWatchEvent(FolderWatchEventKind.Renamed, "Photos2", "Photos") { IsGone = true });
        bed.Factory.Raise(@"E:\Library", new FolderWatchEvent(FolderWatchEventKind.Renamed, "Photos3", "Photos2", IsDirectory: true));
        bed.Settle();

        var change = Assert.Single(bed.Published);
        Assert.Equal((FolderChangeKind.Renamed, "Photos3/a.jpg", "Photos/a.jpg"), (change.Kind, change.RelativePath, change.OldRelativePath));
    }

    [Fact]
    public async Task GoneRename_ThatWasNoFolder_ReportsNothing()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Library");
        await bed.StartAsync();

        bed.Factory.Raise(@"E:\Library", new FolderWatchEvent(FolderWatchEventKind.Renamed, "notes.bak", "notes.txt") { IsGone = true });
        bed.Settle();

        Assert.Empty(bed.Published);
    }

    /// <summary>
    /// Moves the clock on by the retry wait until the condition holds; a retry's wait starts on a thread-pool thread.
    /// </summary>
    private static Task AdvanceUntilAsync(Bed bed, Func<bool> condition) => AdvanceUntilAsync(bed, FolderWatchManager.BusyRetry, condition);

    private static async Task AdvanceUntilAsync(Bed bed, TimeSpan by, Func<bool> condition)
    {
        bed.Clock.Advance(by);
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < end)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task Dispose_StopsEveryWatcher()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();

        bed.Manager.Dispose();

        Assert.Empty(bed.Factory.Running);
        Assert.Empty(bed.Manager.WatchedFolders);
    }

    [Fact]
    public async Task Changes_OfAWatchedFolder_ArePublishedOnceStable()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();

        bed.Files.Write("a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Created, "a.jpg"));
        bed.Clock.Advance(TimeSpan.FromMinutes(1));

        var published = Assert.Single(bed.Publisher.Published);
        Assert.Equal(folder.Id, published.FolderId);
        Assert.Equal("a.jpg", Assert.Single(published.Changes).RelativePath);
    }

    [Fact]
    public async Task Deletions_InAFolderThatCantBeReached_AreDropped_AndTheFolderIsChecked()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();

        // The drive was unplugged: its files look deleted.
        bed.Files.IsAvailable = false;
        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Deleted, "a.jpg"));
        bed.Settle();
        await bed.Manager.WaitForRecoveryAsync();

        Assert.Empty(bed.Publisher.Published);
        var check = Assert.Single(bed.Sender.Requests.OfType<CheckFolderAvailabilityCommand>());
        Assert.Equal([folder.Id], check.FolderIds!);
    }

    [Fact]
    public async Task Deletions_InAFolderBrieflyUnreachable_ThatTheCheckFindsOnline_RescanTheFolder()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();
        bed.Files.IsAvailable = false;
        bed.Sender.OnCheck = _ =>
        {
            bed.Files.IsAvailable = true;
            return Task.CompletedTask;
        };

        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Created, "new.jpg"));
        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Deleted, "a.jpg"));
        bed.Settle();
        await bed.Manager.WaitForRecoveryAsync();

        Assert.Empty(bed.Publisher.Published);
        Assert.Equal(folder.Id, Assert.Single(bed.Sender.Rescans).FolderId);
    }

    [Fact]
    public async Task Deletions_InAFolderThatTheCheckRelinks_AreLeftToItsCatchUp()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();
        bed.Files.IsAvailable = false;
        bed.Sender.CheckChanged = [folder];

        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Deleted, "a.jpg"));
        bed.Settle();
        await bed.Manager.WaitForRecoveryAsync();

        Assert.Empty(bed.Sender.Rescans);
    }

    [Fact]
    public async Task Deletions_InAFolderThatTheCheckFindsOffline_DontRescanIt()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();
        bed.Files.IsAvailable = false;
        bed.Sender.OnCheck = async _ =>
        {
            folder.MarkOffline(Now);
            await bed.Library.FolderList.UpdateAvailabilityAsync(folder, TestContext.Current.CancellationToken);
        };

        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Deleted, "a.jpg"));
        bed.Settle();
        await bed.Manager.WaitForRecoveryAsync();

        Assert.Empty(bed.Sender.Rescans);
    }

    [Fact]
    public async Task Deletions_InAFolderThatCanBeReached_ArePublished()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();

        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Deleted, "a.jpg"));
        bed.Settle();

        Assert.Equal(FolderChangeKind.Deleted, Assert.Single(bed.Published).Kind);
        Assert.Empty(bed.Sender.Requests.OfType<CheckFolderAvailabilityCommand>());
    }

    [Fact]
    public async Task Changes_OfAFolderNoLongerWatched_AreDropped()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();
        bed.Files.Write("a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Created, "a.jpg"));

        folder.MarkOffline(Now);
        await bed.Library.FolderList.UpdateAvailabilityAsync(folder, TestContext.Current.CancellationToken);
        await bed.Manager.RefreshAsync();
        bed.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Empty(bed.Publisher.Published);
    }

    [Fact]
    public async Task BrowserDownload_IsReportedOnlyAsTheFinishedFile()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Downloads");
        await bed.StartAsync();

        // Chrome writes "clip.mp4.crdownload" and renames it when the download is done.
        for (var chunk = 1; chunk <= 5; chunk++)
        {
            bed.Write(@"E:\Downloads", "clip.mp4.crdownload", chunk * 1000);
            bed.Clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Empty(bed.Published);

        bed.Files.Delete("clip.mp4.crdownload");
        bed.Files.Write("clip.mp4", 5000, bed.Clock.GetUtcNow());
        bed.Factory.Raise(@"E:\Downloads", new FolderWatchEvent(FolderWatchEventKind.Renamed, "clip.mp4", "clip.mp4.crdownload"));
        bed.Settle();

        var change = Assert.Single(bed.Published);
        Assert.Equal((FolderChangeKind.Changed, "clip.mp4", (string?)null), (change.Kind, change.RelativePath, change.OldRelativePath));
    }

    [Fact]
    public async Task OfficeLockFiles_AndOtherIgnoredFiles_AreNeverReported()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();

        bed.Write(@"E:\Photos", "~$Holiday.mp4");
        bed.Write(@"E:\Photos", "notes.txt");
        bed.Write(@"E:\Photos", "a.jpg.tmp");
        bed.Settle();

        Assert.Empty(bed.Published);
    }

    [Fact]
    public async Task IgnoreList_Changed_AppliesToNewEvents()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\Photos");
        await bed.StartAsync();

        bed.Settings.SetValue(MonitoringSettings.IgnorePatterns, "*.jpg");
        bed.Write(@"E:\Photos", "a.jpg");
        bed.Write(@"E:\Photos", "b.png");
        bed.Settle();

        Assert.Equal(["b.png"], bed.Published.Select(change => change.RelativePath));
    }

    [Fact]
    public async Task FolderRules_ExcludedSubfoldersAndHiddenFiles_AreNotReported()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        folder.ChangeRules(new FolderRules { ExcludePatterns = [FolderRules.ForSubfolder("raw")] });
        bed.Library.FolderList.Seed(folder);
        await bed.StartAsync();

        bed.Write(@"E:\Photos", "raw/a.jpg");
        bed.Files.Write("hidden.jpg", 10, bed.Clock.GetUtcNow(), isHidden: true);
        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Created, "hidden.jpg"));
        bed.Write(@"E:\Photos", "kept.jpg");

        // Moved out of the excluded folder: a new file. Moved into it: gone from the library's view.
        bed.Files.Write("moved-out.jpg", 10, bed.Clock.GetUtcNow());
        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Renamed, "moved-out.jpg", "raw/moved-out.jpg"));
        bed.Factory.Raise(@"E:\Photos", new FolderWatchEvent(FolderWatchEventKind.Renamed, "raw/moved-in.jpg", "moved-in.jpg"));
        bed.Settle();

        Assert.Equal(
            [(FolderChangeKind.Changed, "kept.jpg"), (FolderChangeKind.Deleted, "moved-in.jpg"), (FolderChangeKind.Changed, "moved-out.jpg")],
            bed.Published.Select(change => (change.Kind, change.RelativePath)).OrderBy(change => change.RelativePath, StringComparer.Ordinal));
    }

    [Fact]
    public async Task CloudPlaceholder_IsNeitherOpenedNorReported_UntilItIsMadeLocal()
    {
        var bed = new Bed();
        await bed.AddFolderAsync(@"E:\OneDrive");
        await bed.StartAsync();
        var modified = bed.Clock.GetUtcNow();

        bed.Files.Write("cloud.jpg", 10, modified, isCloudPlaceholder: true);
        bed.Factory.Raise(@"E:\OneDrive", new FolderWatchEvent(FolderWatchEventKind.Created, "cloud.jpg"));
        bed.Settle();
        Assert.Empty(bed.Published);
        Assert.Equal(0, bed.Files.Opens);

        // "Always keep on this device": only the attributes change.
        bed.Files.Write("cloud.jpg", 10, modified);
        bed.Factory.Raise(@"E:\OneDrive", new FolderWatchEvent(FolderWatchEventKind.Changed, "cloud.jpg"));
        bed.Settle();

        Assert.Equal([(FolderChangeKind.Changed, "cloud.jpg")], bed.Published.Select(change => (change.Kind, change.RelativePath)));
    }

    [Fact]
    public async Task PartialFolder_ReportsOnlyItsTrackedFiles()
    {
        var bed = new Bed();
        var folder = IndexedFolder.CreateForSelectedFiles(bed.Library.Library.Id, @"E:\Picked", @"E:\Picked", Now);
        await bed.Library.FolderList.AddAsync(folder, TestContext.Current.CancellationToken);
        bed.Library.ItemList.Items.Add(MediaItem.Discover(folder.Id, RelativePath.From("tracked.jpg"), MediaType.Image, 10, Now, Now, null, Now));
        await bed.StartAsync();

        bed.Write(@"E:\Picked", "tracked.jpg", 20);
        bed.Write(@"E:\Picked", "other.jpg");
        bed.Settle();

        Assert.Equal(["tracked.jpg"], bed.Published.Select(change => change.RelativePath));
    }

    [Fact]
    public async Task ExcludedItems_AreNotReported()
    {
        var bed = new Bed();
        var folder = await bed.AddFolderAsync(@"E:\Photos");
        var removed = MediaItem.Discover(folder.Id, RelativePath.From("removed.jpg"), MediaType.Image, 10, Now, Now, null, Now);
        removed.ChangeStatus(MediaItemStatus.Excluded, Now, "Removed by the user.");
        bed.Library.ItemList.Items.Add(removed);
        await bed.StartAsync();

        bed.Write(@"E:\Photos", "removed.jpg", 20);
        bed.Write(@"E:\Photos", "new.jpg");
        bed.Settle();

        Assert.Equal(["new.jpg"], bed.Published.Select(change => change.RelativePath));
    }

    /// <summary>
    /// A manager on a fake library, with watchers that record whether they run.
    /// </summary>
    private sealed class Bed
    {
        public Bed()
        {
            Library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "watching"), "watching");
            Manager = new FolderWatchManager(
                Session,
                Files,
                Publisher,
                Sender,
                Settings,
                new MediaExtensions(),
                Clock,
                NullLogger<FolderWatchManager>.Instance,
                Factory,
                Pause,
                new FolderLocations(NullLogger<FolderLocations>.Instance, Probe));
        }

        public FakeLibrarySession Session { get; } = new();

        public ManualClock Clock { get; } = new();

        public MonitoringPause Pause { get; } = new();

        public InMemoryMediaFileSource Files { get; } = new();

        public RecordingPublisher Publisher { get; } = new();

        public RecordingSender Sender { get; } = new();

        public InMemorySettingsStore Settings { get; } = new();

        public FakeFolderLocationProbe Probe { get; } = new();

        public IEnumerable<FolderChange> Published => Publisher.Published.SelectMany(batch => batch.Changes);

        /// <summary>
        /// Writes a file and raises its event on the folder's watcher.
        /// </summary>
        public void Write(string folder, string path, long length = 10)
        {
            Files.Write(path, length, Clock.GetUtcNow());
            Factory.Raise(folder, new FolderWatchEvent(FolderWatchEventKind.Created, path));
        }

        public void Settle() => Clock.Advance(TimeSpan.FromMinutes(1));

        public FakeOpenLibrary Library { get; }

        public RecordingWatcherFactory Factory { get; } = new();

        public FolderWatchManager Manager { get; }

        public async Task<IndexedFolder> AddFolderAsync(string path, MonitoringMode mode = MonitoringMode.Auto)
        {
            var folder = IndexedFolder.Create(Library.Library.Id, path, path, Now);
            folder.ChangeMonitoringMode(mode);
            await Library.FolderList.AddAsync(folder, TestContext.Current.CancellationToken);
            return folder;
        }

        public async Task StartAsync()
        {
            await Session.SetCurrentAsync(Library);
            Manager.Start();
            await Manager.RefreshAsync();
        }
    }

    /// <summary>
    /// Creates watchers that only record whether they're running.
    /// </summary>
    private sealed class RecordingWatcherFactory : IFolderWatcherFactory
    {
        private readonly List<Watcher> watchers = [];

        public HashSet<string> Failing { get; } = [];

        public int Started => watchers.Count;

        public IReadOnlyList<string> Running => [.. watchers.Where(watcher => !watcher.Disposed).Select(watcher => watcher.Folder.Id)];

        public void Raise(string folder, FolderWatchEvent change) => Latest(folder).Raise(change);

        public void Fail(string folder, bool overflow) => Latest(folder).Fail(overflow);

        public IFolderWatcher Watch(FolderHandle folder)
        {
            if (Failing.Contains(folder.Id))
            {
                throw new DirectoryNotFoundException(folder.Id);
            }

            var watcher = new Watcher(folder);
            watchers.Add(watcher);
            return watcher;
        }

        private Watcher Latest(string folder) => watchers.Last(watcher => watcher.Folder.Id == folder);

        private sealed class Watcher(FolderHandle folder) : IFolderWatcher
        {
            public event EventHandler<FolderWatchEvent>? Changed;

            public event EventHandler<FolderWatchErrorEventArgs>? Failed;

            public FolderHandle Folder { get; } = folder;

            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;

            public void Raise(FolderWatchEvent change) => Changed?.Invoke(this, change);

            public void Fail(bool overflow) => Failed?.Invoke(this, new FolderWatchErrorEventArgs(overflow, overflow ? new InternalBufferOverflowException() : new IOException("The network name is no longer available.")));
        }
    }

    /// <summary>
    /// Records the requests, answers rescans with the queued outcomes, then with <see cref="Then"/>.
    /// </summary>
    private sealed class RecordingSender : FakeSender
    {
        public ConcurrentQueue<object> Requests { get; } = new();

        public ConcurrentQueue<StartRescanOutcome> Outcomes { get; } = new();

        public StartRescanOutcome Then { get; set; } = StartRescanOutcome.Started;

        public Func<CheckFolderAvailabilityCommand, Task>? OnCheck { get; set; }

        public IReadOnlyList<IndexedFolder> CheckChanged { get; set; } = [];

        public IEnumerable<StartRescanCommand> Rescans => Requests.OfType<StartRescanCommand>();

        public override async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);
            switch (request)
            {
                case StartRescanCommand:
                    var outcome = Outcomes.TryDequeue(out var next) ? next : Then;
                    return (TResponse)(object)new StartRescanResult(null, outcome);
                case CheckFolderAvailabilityCommand check:
                    if (OnCheck is not null)
                    {
                        await OnCheck(check);
                    }

                    return (TResponse)(object)CheckChanged;
                default:
                    throw new NotSupportedException(request.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Records the published notifications.
    /// </summary>
    private sealed class RecordingPublisher : FakePublisher
    {
        public IReadOnlyList<FolderChangesDetected> Published => Of<FolderChangesDetected>();
    }

    /// <summary>
    /// Treats images and videos by their extension as media files.
    /// </summary>
    private sealed class MediaExtensions : IMediaFormatRegistry
    {
        private static readonly string[] Extensions = [".jpg", ".png", ".mp4"];

        public IReadOnlyList<IMediaFormatHandler> Handlers => [];

        public bool IsCandidate(string fileName) => Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

        public IMediaFormatHandler? FindByFormatId(string formatId) => null;

        public Task<MediaFormatDetection> DetectAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Detection isn't needed for watching.");
    }
}
