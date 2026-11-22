using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Monitoring;
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
            Manager = new FolderWatchManager(Session, Files, Publisher, Settings, new MediaExtensions(), Clock, NullLogger<FolderWatchManager>.Instance, Factory);
        }

        public FakeLibrarySession Session { get; } = new();

        public ManualClock Clock { get; } = new();

        public InMemoryMediaFileSource Files { get; } = new();

        public RecordingPublisher Publisher { get; } = new();

        public InMemorySettingsStore Settings { get; } = new();

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

        public async Task<IndexedFolder> AddFolderAsync(string path)
        {
            var folder = IndexedFolder.Create(Library.Library.Id, path, path, Now);
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
    /// Records the published notifications.
    /// </summary>
    private sealed class RecordingPublisher : IPublisher
    {
        public List<FolderChangesDetected> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            notification is INotification typed ? Publish(typed, cancellationToken) : Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            if (notification is FolderChangesDetected detected)
            {
                Published.Add(detected);
            }

            return Task.CompletedTask;
        }
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
