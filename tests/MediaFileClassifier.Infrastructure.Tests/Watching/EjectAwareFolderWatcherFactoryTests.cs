using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Infrastructure.Files;

namespace MediaFileClassifier.Infrastructure.Tests.Watching;

/// <summary>
/// Tests for <see cref="EjectAwareFolderWatcherFactory"/> (E04-S04-T01) with a fake eject source: an eject request
/// releases the folder before it returns, a cancelled eject watches it again and reports lost events, and events of a
/// released watcher are dropped.
/// </summary>
public sealed class EjectAwareFolderWatcherFactoryTests
{
    private static readonly FolderHandle Folder = new(@"E:\Photos", "Photos");

    [Fact]
    public void Watch_ForwardsTheInnerWatchersEvents_AndRegistersTheFolder()
    {
        var bed = new Bed();
        using var watcher = bed.Factory.Watch(Folder);
        var changes = new List<FolderWatchEvent>();
        var failures = new List<FolderWatchErrorEventArgs>();
        watcher.Changed += (_, change) => changes.Add(change);
        watcher.Failed += (_, failure) => failures.Add(failure);

        bed.Inner.Watchers[0].RaiseChanged(new FolderWatchEvent(FolderWatchEventKind.Created, "a.jpg"));
        bed.Inner.Watchers[0].RaiseFailed(new FolderWatchErrorEventArgs(isOverflow: true, exception: null));

        Assert.Equal("a.jpg", Assert.Single(changes).RelativePath);
        Assert.True(Assert.Single(failures).IsOverflow);
        Assert.Equal(Folder, Assert.Single(bed.Ejects.Registrations).Folder);
    }

    [Fact]
    public void EjectRequest_DisposesTheInnerWatcher_BeforeItReturns_AndDropsItsLateEvents()
    {
        var bed = new Bed();
        using var watcher = bed.Factory.Watch(Folder);
        var changes = new List<FolderWatchEvent>();
        watcher.Changed += (_, change) => changes.Add(change);

        bed.Ejects.Registrations[0].RequestEject();
        bed.Inner.Watchers[0].RaiseChanged(new FolderWatchEvent(FolderWatchEventKind.Created, "late.jpg"));

        Assert.True(bed.Inner.Watchers[0].IsDisposed);
        Assert.Empty(changes);
    }

    [Fact]
    public void CancelledEject_WatchesTheFolderAgain_AndReportsLostEvents()
    {
        var bed = new Bed();
        using var watcher = bed.Factory.Watch(Folder);
        var changes = new List<FolderWatchEvent>();
        var failures = new List<FolderWatchErrorEventArgs>();
        watcher.Changed += (_, change) => changes.Add(change);
        watcher.Failed += (_, failure) => failures.Add(failure);

        bed.Ejects.Registrations[0].RequestEject();
        bed.Ejects.Registrations[0].CancelEject();
        bed.Inner.Watchers[1].RaiseChanged(new FolderWatchEvent(FolderWatchEventKind.Created, "b.jpg"));

        Assert.Equal(2, bed.Inner.Watchers.Count);
        Assert.False(bed.Inner.Watchers[1].IsDisposed);
        var failure = Assert.Single(failures);
        Assert.True(failure.IsOverflow);
        Assert.Equal("b.jpg", Assert.Single(changes).RelativePath);
    }

    [Fact]
    public void CancelledEject_OfAFolderThatCantBeWatched_ReportsAFailure()
    {
        var bed = new Bed();
        using var watcher = bed.Factory.Watch(Folder);
        var failures = new List<FolderWatchErrorEventArgs>();
        watcher.Failed += (_, failure) => failures.Add(failure);

        bed.Ejects.Registrations[0].RequestEject();
        bed.Inner.FailNext = true;
        bed.Ejects.Registrations[0].CancelEject();

        var failure = Assert.Single(failures);
        Assert.False(failure.IsOverflow);
        Assert.IsType<DirectoryNotFoundException>(failure.Exception);
    }

    [Fact]
    public void CancelWithoutARequest_AndARepeatedRequest_DoNothing()
    {
        var bed = new Bed();
        using var watcher = bed.Factory.Watch(Folder);
        var failures = new List<FolderWatchErrorEventArgs>();
        watcher.Failed += (_, failure) => failures.Add(failure);

        bed.Ejects.Registrations[0].CancelEject();
        bed.Ejects.Registrations[0].RequestEject();
        bed.Ejects.Registrations[0].RequestEject();

        Assert.Single(bed.Inner.Watchers);
        Assert.Empty(failures);
    }

    [Fact]
    public void Dispose_DisposesTheWatcherAndTheRegistration_AndIgnoresLaterEjects()
    {
        var bed = new Bed();
        var watcher = bed.Factory.Watch(Folder);

        watcher.Dispose();
        bed.Ejects.Registrations[0].RequestEject();
        bed.Ejects.Registrations[0].CancelEject();

        Assert.True(bed.Inner.Watchers[0].IsDisposed);
        Assert.True(bed.Ejects.Registrations[0].IsDisposed);
        Assert.Single(bed.Inner.Watchers);
    }

    [Fact]
    public void Watch_WithoutARegistration_StillWatches()
    {
        var bed = new Bed();
        bed.Ejects.Unsupported = true;

        using var watcher = bed.Factory.Watch(Folder);

        Assert.Single(bed.Inner.Watchers);
        Assert.Empty(bed.Ejects.Registrations);
    }

    [Fact]
    public void Watch_OfAFolderThatCantBeWatched_Throws_WithoutRegistering()
    {
        var bed = new Bed();
        bed.Inner.FailNext = true;

        Assert.Throws<DirectoryNotFoundException>(() => bed.Factory.Watch(Folder));
        Assert.Empty(bed.Ejects.Registrations);
    }

    private sealed class Bed
    {
        public Bed() => Factory = new EjectAwareFolderWatcherFactory(Inner, Ejects);

        public ScriptedWatcherFactory Inner { get; } = new();

        public ScriptedEjects Ejects { get; } = new();

        public EjectAwareFolderWatcherFactory Factory { get; }
    }

    private sealed class ScriptedWatcherFactory : IFolderWatcherFactory
    {
        public List<ScriptedWatcher> Watchers { get; } = [];

        public bool FailNext { get; set; }

        public IFolderWatcher Watch(FolderHandle folder)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new DirectoryNotFoundException(folder.DisplayName);
            }

            var watcher = new ScriptedWatcher();
            Watchers.Add(watcher);
            return watcher;
        }
    }

    private sealed class ScriptedWatcher : IFolderWatcher
    {
        public event EventHandler<FolderWatchEvent>? Changed;

        public event EventHandler<FolderWatchErrorEventArgs>? Failed;

        public bool IsDisposed { get; private set; }

        public void RaiseChanged(FolderWatchEvent change) => Changed?.Invoke(this, change);

        public void RaiseFailed(FolderWatchErrorEventArgs failure) => Failed?.Invoke(this, failure);

        public void Dispose() => IsDisposed = true;
    }

    private sealed class ScriptedEjects : IEjectNotifications
    {
        public List<ScriptedRegistration> Registrations { get; } = [];

        public bool Unsupported { get; set; }

        public IEjectRegistration? Register(FolderHandle folder)
        {
            if (Unsupported)
            {
                return null;
            }

            var registration = new ScriptedRegistration(folder);
            Registrations.Add(registration);
            return registration;
        }
    }

    private sealed class ScriptedRegistration(FolderHandle folder) : IEjectRegistration
    {
        public event EventHandler? EjectRequested;

        public event EventHandler? EjectCancelled;

        public FolderHandle Folder { get; } = folder;

        public bool IsDisposed { get; private set; }

        public void RequestEject() => EjectRequested?.Invoke(this, EventArgs.Empty);

        public void CancelEject() => EjectCancelled?.Invoke(this, EventArgs.Empty);

        public void Dispose() => IsDisposed = true;
    }
}
