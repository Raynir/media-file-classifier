using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Turns raw watcher events into stable changes (E04-S01-T02, DNEW-3). A path is checked once its events have been quiet
/// for <see cref="Quiet"/>. A file is ready when its size and modification time are the same as at a check
/// <see cref="StableFor"/> earlier and it opens for reading; until then it's checked every <see cref="StableFor"/>, and
/// while it stays locked the wait doubles up to <see cref="MaxWait"/>. An empty file isn't reported (a browser's
/// placeholder while it downloads); the event that fills it is. A new folder is listed and its files are checked like
/// new files; a renamed folder is reported as a rename of each of its files. Checks run one at a time on the clock's
/// timer, so a burst of events gives one change per file.
/// </summary>
public sealed class FolderChangeBuffer : IDisposable
{
    /// <summary>
    /// How long a path's events must be quiet before it's checked.
    /// </summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a file's size and modification time must stay the same.
    /// </summary>
    public static readonly TimeSpan StableFor = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The longest wait between checks of a file that stays locked.
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often due paths are looked for while any are waiting.
    /// </summary>
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(500);

    private readonly IMediaFileSource files;
    private readonly TimeProvider clock;
    private readonly ILogger logger;
    private readonly Func<Id<IndexedFolder>, IReadOnlyList<FolderChange>, CancellationToken, Task> ready;
    private readonly Lock syncRoot = new();
    private readonly Dictionary<Id<IndexedFolder>, Dictionary<string, Pending>> pending = [];
    private readonly ITimer timer;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Cancelled by Dispose; a check already under way may still read its token. It has no timer or wait handle.")]
    private readonly CancellationTokenSource stopping = new();

    private bool ticking;
    private int processing;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderChangeBuffer"/> class.
    /// </summary>
    /// <param name="files">Checks the files.</param>
    /// <param name="clock">Times the waits.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="ready">Receives each folder's changes that became stable at one check.</param>
    public FolderChangeBuffer(
        IMediaFileSource files,
        TimeProvider clock,
        ILogger logger,
        Func<Id<IndexedFolder>, IReadOnlyList<FolderChange>, CancellationToken, Task> ready)
    {
        this.files = files;
        this.clock = clock;
        this.logger = logger;
        this.ready = ready;
        timer = clock.CreateTimer(_ => _ = ProcessAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Gets how many paths are waiting.
    /// </summary>
    public int PendingCount
    {
        get
        {
            lock (syncRoot)
            {
                return pending.Values.Sum(paths => paths.Count);
            }
        }
    }

    /// <summary>
    /// Records a raw event. Each event for a path starts its quiet wait again.
    /// </summary>
    /// <param name="folderId">The watched folder.</param>
    /// <param name="folder">Its handle, to check the files through.</param>
    /// <param name="change">The event.</param>
    public void Record(Id<IndexedFolder> folderId, FolderHandle folder, FolderWatchEvent change)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(change);

        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            if (!pending.TryGetValue(folderId, out var paths))
            {
                paths = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);
                pending[folderId] = paths;
            }

            var due = clock.GetUtcNow() + Quiet;
            paths.TryGetValue(change.RelativePath, out var existing);
            switch (change.Kind)
            {
                case FolderWatchEventKind.Created or FolderWatchEventKind.Changed when change.IsDirectory:
                    // A folder's own write time changes with its content; only a new folder needs listing.
                    if (change.Kind == FolderWatchEventKind.Created)
                    {
                        paths[change.RelativePath] = new Pending(folder, FolderChangeKind.Changed, null, isDirectory: true, due);
                    }

                    break;

                case FolderWatchEventKind.Created or FolderWatchEventKind.Changed:
                    // A file renamed and then written is still the renamed file.
                    paths[change.RelativePath] = existing is { Kind: FolderChangeKind.Renamed, IsDirectory: false }
                        ? new Pending(folder, FolderChangeKind.Renamed, existing.OldPath, isDirectory: false, due)
                        : new Pending(folder, FolderChangeKind.Changed, null, isDirectory: false, due);
                    break;

                case FolderWatchEventKind.Deleted:
                    paths[change.RelativePath] = new Pending(folder, FolderChangeKind.Deleted, null, isDirectory: false, due);
                    if (existing is { Kind: FolderChangeKind.Renamed, OldPath: { } renamedFrom })
                    {
                        // Moved and then deleted: the file at the old path is gone too.
                        paths[renamedFrom] = new Pending(folder, FolderChangeKind.Deleted, null, isDirectory: false, due);
                    }

                    break;

                case FolderWatchEventKind.Renamed:
                    var oldPath = change.OldRelativePath ?? change.RelativePath;
                    if (paths.Remove(oldPath, out var moved) && moved is { Kind: FolderChangeKind.Renamed, OldPath: { } earlier })
                    {
                        // Renamed twice before it settled: one rename from where it was first.
                        oldPath = earlier;
                    }

                    paths[change.RelativePath] = new Pending(folder, FolderChangeKind.Renamed, oldPath, change.IsDirectory, due);
                    break;
            }

            if (paths.Count == 0)
            {
                pending.Remove(folderId);
            }

            Arm();
        }
    }

    /// <summary>
    /// Drops what's waiting for a folder, e.g. when it stops being watched.
    /// </summary>
    /// <param name="folderId">The folder.</param>
    public void Forget(Id<IndexedFolder> folderId)
    {
        lock (syncRoot)
        {
            pending.Remove(folderId);
        }
    }

    /// <summary>
    /// Stops the checks and drops everything waiting.
    /// </summary>
    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pending.Clear();
        }

        timer.Dispose();
        stopping.Cancel();
    }

    private static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromTicks(Math.Min(MaxWait.Ticks, StableFor.Ticks << Math.Min(attempts, 8)));

    // Called under the lock.
    private void Arm()
    {
        var any = pending.Count > 0;
        if (any != ticking && !disposed)
        {
            ticking = any;
            timer.Change(any ? Tick : Timeout.InfiniteTimeSpan, any ? Tick : Timeout.InfiniteTimeSpan);
        }
    }

    private async Task ProcessAsync()
    {
        // A slow check (a network share) makes later ticks skip, not pile up.
        if (Interlocked.Exchange(ref processing, 1) == 1)
        {
            return;
        }

        var token = stopping.Token;
        try
        {
            List<(Id<IndexedFolder> FolderId, string Path, Pending Entry)> due;
            lock (syncRoot)
            {
                var now = clock.GetUtcNow();
                due = [.. pending.SelectMany(folder => folder.Value
                    .Where(path => path.Value.DueAt <= now)
                    .Select(path => (folder.Key, path.Key, path.Value)))];
            }

            var found = new Dictionary<Id<IndexedFolder>, List<FolderChange>>();
            foreach (var (folderId, path, entry) in due)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var changes = await CheckAsync(folderId, path, entry, token).ConfigureAwait(false);
                if (changes.Count > 0)
                {
                    if (!found.TryGetValue(folderId, out var list))
                    {
                        list = [];
                        found[folderId] = list;
                    }

                    list.AddRange(changes);
                }
            }

            foreach (var (folderId, changes) in found)
            {
                try
                {
                    await ready(folderId, changes, token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Couldn't hand over {Count} folder changes", changes.Count);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopping.
        }
        finally
        {
            Volatile.Write(ref processing, 0);
        }
    }

    /// <summary>
    /// Checks one due path. It returns the changes that are ready, and either drops the path or sets its next check.
    /// A path whose events arrived during the check is left to its newer entry.
    /// </summary>
    private async Task<IReadOnlyList<FolderChange>> CheckAsync(Id<IndexedFolder> folderId, string path, Pending entry, CancellationToken token)
    {
        try
        {
            if (entry.IsDirectory)
            {
                var inside = new List<MediaFileStat>();
                await foreach (var file in files.EnumerateFilesAsync(entry.Folder, path, token).ConfigureAwait(false))
                {
                    inside.Add(file);
                }

                return CompleteDirectory(folderId, path, entry, inside);
            }

            var stat = await files.GetStatAsync(new FileLocation(entry.Folder, path), token).ConfigureAwait(false);
            if (stat is null)
            {
                // Gone: a deletion, or a new file deleted again (the library has nothing to drop then).
                var gone = new FolderChange(FolderChangeKind.Deleted, entry.Kind == FolderChangeKind.Renamed ? entry.OldPath! : path);
                return Complete(folderId, path, entry, gone);
            }

            if (stat.Length == 0)
            {
                return Complete(folderId, path, entry, null);
            }

            if (entry.LastStat is not { } last || last.Length != stat.Length || last.LastModifiedUtc != stat.LastModifiedUtc)
            {
                // A deletion whose path is back (a safe save) is a change of the file.
                var kind = entry.Kind == FolderChangeKind.Deleted ? FolderChangeKind.Changed : entry.Kind;
                Reschedule(folderId, path, entry, entry.With(kind, stat, entry.Attempts, clock.GetUtcNow() + StableFor));
                return [];
            }

            await using (await files.OpenReadAsync(new FileLocation(entry.Folder, path), token).ConfigureAwait(false))
            {
                // Opening is the check: a file still being written is locked.
            }

            return Complete(folderId, path, entry, new FolderChange(entry.Kind, path, entry.OldPath, stat));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Locked or briefly unreachable: wait longer each time. A folder that's gone raises a watcher error.
            Reschedule(folderId, path, entry, entry.With(entry.Kind, entry.LastStat, entry.Attempts + 1, clock.GetUtcNow() + Backoff(entry.Attempts + 1)));
            return [];
        }
    }

    private IReadOnlyList<FolderChange> CompleteDirectory(Id<IndexedFolder> folderId, string path, Pending entry, List<MediaFileStat> inside)
    {
        if (entry.Kind == FolderChangeKind.Renamed)
        {
            // A rename is instant, so the files are as stable as they were before it.
            var prefix = entry.OldPath + "/";
            var renames = inside
                .Select(file => new FolderChange(FolderChangeKind.Renamed, file.RelativePath, prefix + file.RelativePath[(path.Length + 1)..], file))
                .ToList();
            return Complete(folderId, path, entry, null, renames);
        }

        // A new folder, e.g. moved in from elsewhere: its files are checked like new files.
        lock (syncRoot)
        {
            if (!IsCurrent(folderId, path, entry))
            {
                return [];
            }

            var paths = pending[folderId];
            paths.Remove(path);
            var due = clock.GetUtcNow() + Quiet;
            foreach (var file in inside)
            {
                paths.TryAdd(file.RelativePath, new Pending(entry.Folder, FolderChangeKind.Changed, null, isDirectory: false, due));
            }

            RemoveIfEmpty(folderId);
            return [];
        }
    }

    private IReadOnlyList<FolderChange> Complete(Id<IndexedFolder> folderId, string path, Pending entry, FolderChange? change, IReadOnlyList<FolderChange>? more = null)
    {
        lock (syncRoot)
        {
            if (!IsCurrent(folderId, path, entry))
            {
                return [];
            }

            pending[folderId].Remove(path);
            RemoveIfEmpty(folderId);
        }

        return change is null ? more ?? [] : [change];
    }

    private void Reschedule(Id<IndexedFolder> folderId, string path, Pending entry, Pending next)
    {
        lock (syncRoot)
        {
            if (IsCurrent(folderId, path, entry))
            {
                pending[folderId][path] = next;
            }
        }
    }

    // Called under the lock.
    private bool IsCurrent(Id<IndexedFolder> folderId, string path, Pending entry) =>
        pending.TryGetValue(folderId, out var paths) && paths.TryGetValue(path, out var current) && ReferenceEquals(current, entry);

    // Called under the lock.
    private void RemoveIfEmpty(Id<IndexedFolder> folderId)
    {
        if (pending.TryGetValue(folderId, out var paths) && paths.Count == 0)
        {
            pending.Remove(folderId);
        }

        Arm();
    }

    /// <summary>
    /// A path waiting to be checked. It's replaced, never changed, so a check can tell whether events arrived meanwhile.
    /// </summary>
    private sealed class Pending(FolderHandle folder, FolderChangeKind kind, string? oldPath, bool isDirectory, DateTimeOffset dueAt)
    {
        public FolderHandle Folder { get; } = folder;

        public FolderChangeKind Kind { get; } = kind;

        public string? OldPath { get; } = oldPath;

        public bool IsDirectory { get; } = isDirectory;

        public DateTimeOffset DueAt { get; } = dueAt;

        public MediaFileStat? LastStat { get; private init; }

        public int Attempts { get; private init; }

        public Pending With(FolderChangeKind kind, MediaFileStat? stat, int attempts, DateTimeOffset dueAt) =>
            new(Folder, kind, OldPath, IsDirectory, dueAt) { LastStat = stat, Attempts = attempts };
    }
}
