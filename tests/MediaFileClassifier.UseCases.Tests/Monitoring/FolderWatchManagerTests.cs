using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Testing.Files;
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

    /// <summary>
    /// A manager on a fake library, with watchers that record whether they run.
    /// </summary>
    private sealed class Bed
    {
        public Bed()
        {
            Library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "watching"), "watching");
            Manager = new FolderWatchManager(Session, Files, Publisher, Clock, NullLogger<FolderWatchManager>.Instance, Factory);
        }

        public FakeLibrarySession Session { get; } = new();

        public ManualClock Clock { get; } = new();

        public InMemoryMediaFileSource Files { get; } = new();

        public RecordingPublisher Publisher { get; } = new();

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
}
