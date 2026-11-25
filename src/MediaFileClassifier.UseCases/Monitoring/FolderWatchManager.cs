using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Keeps one recursive watcher on each Online folder of the open library that runs in Watch mode (E04-S01-T01,
/// E04-S05-T01; see <see cref="FolderLocations"/>); folders in Poll or Manual mode aren't watched. The watchers follow the
/// library opening and closing, folders being added, changed, merged or removed, and folders going Offline and coming
/// back. A folder being removed isn't watched. Where the platform can't watch folders (no
/// <see cref="IFolderWatcherFactory"/>), it does nothing. Raw events go through a <see cref="FolderChangeBuffer"/>, and
/// the stable changes are published as <see cref="FolderChangesDetected"/> (E04-S01-T02). Paths the ignore list
/// (<see cref="MonitoringSettings.IgnorePatterns"/>), the folder's rules or the supported formats leave out are dropped
/// as they arrive; a partial folder (D012) reports only its tracked files, and changes to excluded items (E02-S10) aren't
/// reported (E04-S01-T03). When a watcher overflows, the folder is rescanned (E02-S04); when it fails, the folder's
/// availability is checked, and if it's still Online it's watched again and rescanned. A watcher that fails again soon
/// after is started again only after <see cref="FirstFailureWait"/>, doubling up to <see cref="MaxFailureWait"/>. A rescan
/// refused because the folder's import or rescan work is running is asked for again every <see cref="BusyRetry"/>
/// (E04-S01-T04).
/// </summary>
public sealed class FolderWatchManager : IDisposable
{
    /// <summary>
    /// How long to wait before asking again for a rescan that was refused because other work of the folder is running.
    /// </summary>
    public static readonly TimeSpan BusyRetry = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a folder whose watcher failed again soon after a restart waits before it's watched again. Each failure in
    /// a row doubles it.
    /// </summary>
    public static readonly TimeSpan FirstFailureWait = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The longest wait before a failing folder is watched again. A watcher that runs this long without failing starts
    /// the count again.
    /// </summary>
    public static readonly TimeSpan MaxFailureWait = TimeSpan.FromMinutes(30);

    private readonly ILibrarySession session;
    private readonly IMediaFileSource files;
    private readonly IFolderWatcherFactory? factory;
    private readonly FolderLocations? locations;
    private readonly IPublisher publisher;
    private readonly ISender sender;
    private readonly TimeProvider clock;
    private readonly ISettingsStore settings;
    private readonly IMediaFormatRegistry formats;
    private readonly ILogger<FolderWatchManager> logger;
    private readonly FolderChangeBuffer buffer;
    private readonly Lock syncRoot = new();
    private readonly Dictionary<Id<IndexedFolder>, WatchedFolder> watched = [];
    private readonly HashSet<Id<IndexedFolder>> rescansWanted = [];
    private readonly HashSet<Task> recoveries = [];
    private readonly Dictionary<Id<IndexedFolder>, Failures> failures = [];
    private readonly HashSet<Id<IndexedFolder>> online = [];
    private readonly Dictionary<Id<IndexedFolder>, MonitoringMode> notWatched = [];
    private readonly MonitoringPause? pause;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A refresh already under way may still use it after Dispose; it holds no OS handle unless its wait handle is read.")]
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Cancelled by Dispose; a refresh already under way may still read its token. It has no timer or wait handle.")]
    private readonly CancellationTokenSource stopping = new();

    private volatile IgnoreList ignore;
    private IOpenLibrary? library;
    private IFolderRepository? folderEvents;
    private bool started;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderWatchManager"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="files">Checks that changed files are stable.</param>
    /// <param name="publisher">Publishes the stable changes.</param>
    /// <param name="sender">Sends the rescans and availability checks that recover lost events.</param>
    /// <param name="settings">Holds the ignore list.</param>
    /// <param name="formats">Tells which files are media files.</param>
    /// <param name="clock">Times the debounce, the stability checks and the rescan retries.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="factory">Starts the watchers, or <see langword="null"/> where the platform can't watch folders.</param>
    /// <param name="pause">Pauses monitoring for the whole app, or <see langword="null"/> for no pause.</param>
    /// <param name="locations">Tells each folder's monitoring mode; without it, every folder whose own mode isn't Poll or
    /// Manual is watched.</param>
    public FolderWatchManager(
        ILibrarySession session,
        IMediaFileSource files,
        IPublisher publisher,
        ISender sender,
        ISettingsStore settings,
        IMediaFormatRegistry formats,
        TimeProvider clock,
        ILogger<FolderWatchManager> logger,
        IFolderWatcherFactory? factory = null,
        MonitoringPause? pause = null,
        FolderLocations? locations = null)
    {
        this.pause = pause;
        this.session = session;
        this.files = files;
        this.publisher = publisher;
        this.sender = sender;
        this.clock = clock;
        this.settings = settings;
        this.formats = formats;
        ignore = new IgnoreList(settings.Get(MonitoringSettings.IgnorePatterns));
        this.logger = logger;
        this.factory = factory;
        this.locations = locations;
        buffer = new FolderChangeBuffer(files, clock, logger, PublishAsync, IncludesFile);
    }

    /// <summary>
    /// Raised after the state of some folder may have changed: watchers started or stopped, monitoring was turned on or
    /// off or paused or resumed. It can be raised from any thread.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Gets how an Online folder of the open library is monitored now (E04-S06-T01).
    /// </summary>
    /// <param name="folderId">The folder.</param>
    /// <returns>
    /// The state: <see cref="FolderMonitoringState.Polling"/> or <see cref="FolderMonitoringState.Manual"/> for a folder
    /// that runs in that mode (E04-S05); <see cref="FolderMonitoringState.Off"/> for a folder that isn't Online or while
    /// monitoring is off for the library.
    /// </returns>
    public FolderMonitoringState GetState(Id<IndexedFolder> folderId)
    {
        lock (syncRoot)
        {
            var paused = pause?.IsPaused == true;
            if (watched.ContainsKey(folderId))
            {
                return paused ? FolderMonitoringState.Paused : FolderMonitoringState.Watching;
            }

            if (notWatched.TryGetValue(folderId, out var mode))
            {
                return mode switch
                {
                    MonitoringMode.Poll => paused ? FolderMonitoringState.Paused : FolderMonitoringState.Polling,
                    _ => FolderMonitoringState.Manual,
                };
            }

            return online.Contains(folderId) ? FolderMonitoringState.Error : FolderMonitoringState.Off;
        }
    }

    /// <summary>
    /// Gets the folders watched now.
    /// </summary>
    public IReadOnlyCollection<Id<IndexedFolder>> WatchedFolders
    {
        get
        {
            lock (syncRoot)
            {
                return [.. watched.Keys];
            }
        }
    }

    /// <summary>
    /// Starts following the open library. Calling it again does nothing.
    /// </summary>
    public void Start()
    {
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started || factory is null)
            {
                return;
            }

            started = true;
            session.CurrentChanged += OnLibraryChanged;
            settings.Changed += OnSettingChanged;
            if (pause is not null)
            {
                pause.Changed += OnPauseChanged;
            }
        }

        RequestRefresh();
    }

    /// <summary>
    /// Brings the watchers in line with the open library's Online folders now. Refreshes run one at a time; the folder
    /// list is read inside the refresh, so a change that raised an event while one ran is seen by the next.
    /// </summary>
    /// <returns>A task that completes when the watchers match the folders.</returns>
    public async Task RefreshAsync()
    {
        var token = stopping.Token;
        try
        {
            await refreshGate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var current = session.Current;
            Follow(current);
            if (current is null)
            {
                return;
            }

            // Only folders that run in Watch mode get a watcher (E04-S05); none while monitoring is off for the library.
            // The others keep their mode, so GetState shows them as Polling or Manual.
            var folders = new List<IndexedFolder>();
            var others = new Dictionary<Id<IndexedFolder>, MonitoringMode>();
            if (settings.Get(KnownSettings.MonitoringEnabled))
            {
                foreach (var folder in await IndexedFolders.GetActiveAsync(current, token).ConfigureAwait(false))
                {
                    if (folder.Status != FolderStatus.Online)
                    {
                        continue;
                    }

                    var mode = await EffectiveModeAsync(folder, token).ConfigureAwait(false);
                    if (mode == MonitoringMode.Watch)
                    {
                        folders.Add(folder);
                    }
                    else
                    {
                        others[folder.Id] = mode;
                    }
                }
            }

            if (Apply(current, folders, others))
            {
                PublishMonitoringChanged();
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            // E.g. the library closed during the read. Its closing raises a refresh of its own.
            logger.LogWarning(exception, "Couldn't update the folder watchers");
        }
        finally
        {
            refreshGate.Release();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the manager follows the open library, which it doesn't where the platform can't
    /// watch folders.
    /// </summary>
    public bool IsWatching
    {
        get
        {
            lock (syncRoot)
            {
                return started && !disposed;
            }
        }
    }

    /// <summary>
    /// Waits until the watchers match the open library's Online folders, so a sync that runs after it can't miss a change
    /// made while it lists (E04-S03). Does nothing where nothing is watched.
    /// </summary>
    /// <returns>A task that completes when the watchers are running.</returns>
    public Task EnsureWatchingAsync()
    {
        lock (syncRoot)
        {
            if (!started || disposed)
            {
                return Task.CompletedTask;
            }
        }

        return RefreshAsync();
    }

    /// <summary>
    /// Waits until the rescans and availability checks under way after lost events are done. A rescan waiting for busy
    /// work counts as under way.
    /// </summary>
    /// <returns>A task that completes when no recovery is under way.</returns>
    public async Task WaitForRecoveryAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (syncRoot)
            {
                pending = [.. recoveries];
            }

            if (pending.Length == 0)
            {
                return;
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops every watcher and stops following the library.
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
            session.CurrentChanged -= OnLibraryChanged;
            settings.Changed -= OnSettingChanged;
            if (pause is not null)
            {
                pause.Changed -= OnPauseChanged;
            }
        }

        Follow(null);
        buffer.Dispose();
        stopping.Cancel();
    }

    private void RequestRefresh() => _ = RefreshAsync();

    private static Task<MediaItem?> FindAsync(IOpenLibrary current, Id<IndexedFolder> folderId, string path, CancellationToken cancellationToken) =>
        current.Items.FindByPathAsync(folderId, RelativePath.From(path), cancellationToken);

    private void OnLibraryChanged(object? sender, EventArgs e) => RequestRefresh();

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Setting.Key == MonitoringSettings.IgnorePatterns.Key)
        {
            ignore = new IgnoreList(settings.Get(MonitoringSettings.IgnorePatterns));
        }
        else if (e.Setting.Key == KnownSettings.MonitoringEnabled.Key)
        {
            RequestRefresh();
            if (settings.Get(KnownSettings.MonitoringEnabled))
            {
                // Turned on again: what changed while it was off is found by a quick sync, once the watchers run.
                Track(CatchUpAsync(session.Current));
            }
        }
    }

    private void OnPauseChanged(object? sender, EventArgs e)
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        PublishMonitoringChanged();
    }

    private async Task CatchUpAsync(IOpenLibrary? current)
    {
        var token = stopping.Token;
        try
        {
            await RefreshAsync().ConfigureAwait(false);
            while (ReferenceEquals(session.Current, current) && settings.Get(KnownSettings.MonitoringEnabled))
            {
                if (pause is not null)
                {
                    await pause.WaitWhilePausedAsync(token).ConfigureAwait(false);
                    if (!ReferenceEquals(session.Current, current) || !settings.Get(KnownSettings.MonitoringEnabled))
                    {
                        return;
                    }
                }

                var result = await sender.Send(new StartRescanCommand(Quick: true), token).ConfigureAwait(false);
                if (result.Outcome != StartRescanOutcome.Busy)
                {
                    return;
                }

                await Task.Delay(BusyRetry, clock, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Couldn't catch up after monitoring was turned on");
        }
    }

    private void OnFoldersChanged(object? sender, EventArgs e)
    {
        lock (syncRoot)
        {
            // The repository raises from its own copy of the handlers, so a closed library's event can still arrive.
            if (disposed || !ReferenceEquals(sender, folderEvents))
            {
                return;
            }
        }

        RequestRefresh();
    }

    /// <summary>
    /// Follows the folder events of the open library. When the library changed, every watcher of the old one stops.
    /// </summary>
    private void Follow(IOpenLibrary? current)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                current = null;
            }

            if (ReferenceEquals(current, library))
            {
                return;
            }

            if (folderEvents is not null)
            {
                folderEvents.Changed -= OnFoldersChanged;
                folderEvents.StatusChanged -= OnFoldersChanged;
            }

            foreach (var folder in watched.Values)
            {
                folder.Stop();
                buffer.Forget(folder.Folder.Id);
            }

            watched.Clear();
            online.Clear();
            notWatched.Clear();
            library = current;
            folderEvents = current?.Folders;
            if (folderEvents is not null)
            {
                folderEvents.Changed += OnFoldersChanged;
                folderEvents.StatusChanged += OnFoldersChanged;
            }
        }
    }

    // Returns whether the set of watched folders changed.
    private bool Apply(
        IOpenLibrary current, IReadOnlyList<IndexedFolder> folders, IReadOnlyDictionary<Id<IndexedFolder>, MonitoringMode> others)
    {
        lock (syncRoot)
        {
            // Another library opened during the read; its own refresh is queued.
            if (disposed || !ReferenceEquals(current, library))
            {
                return false;
            }

            var before = watched.Keys.ToHashSet();

            var wanted = folders.ToDictionary(folder => folder.Id);
            online.Clear();
            online.UnionWith(wanted.Keys);
            notWatched.Clear();
            foreach (var (id, mode) in others)
            {
                notWatched[id] = mode;
            }

            // A folder that went Offline or away starts its failure count again when it's back.
            foreach (var id in failures.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
            {
                failures.Remove(id);
            }

            foreach (var gone in watched.Values.Where(folder => !wanted.ContainsKey(folder.Folder.Id)).ToList())
            {
                gone.Stop();
                watched.Remove(gone.Folder.Id);
                buffer.Forget(gone.Folder.Id);
                logger.LogInformation("Stopped watching {Folder}", gone.Folder.DisplayPath);
            }

            foreach (var folder in folders)
            {
                if (watched.TryGetValue(folder.Id, out var existing))
                {
                    if (existing.Root == folder.PlatformHandle)
                    {
                        // Rules or partial-folder mode may have changed; the watcher stays.
                        existing.Folder = folder;
                        continue;
                    }

                    // Relinked to a new root.
                    existing.Stop();
                    watched.Remove(folder.Id);
                    buffer.Forget(folder.Id);
                }

                if (failures.TryGetValue(folder.Id, out var failed) && failed.RetryAt > clock.GetUtcNow())
                {
                    // Waiting after repeated failures; its recovery watches it again.
                    continue;
                }

                if (StartWatching(folder) is { } started)
                {
                    watched[folder.Id] = started;
                }
            }

            return !before.SetEquals(watched.Keys);
        }
    }

    private async Task<MonitoringMode> EffectiveModeAsync(IndexedFolder folder, CancellationToken cancellationToken) =>
        locations is null
            ? folder.MonitoringMode == MonitoringMode.Auto ? MonitoringMode.Watch : folder.MonitoringMode
            : await locations.GetEffectiveModeAsync(folder, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Tells listeners that the watched folders changed (E04-S05). A failing listener doesn't stop the watchers.
    /// </summary>
    private void PublishMonitoringChanged() => _ = Task.Run(async () =>
    {
        try
        {
            await publisher.Publish(new FolderMonitoringChanged(), stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Couldn't report the change of the watched folders");
        }
    });

    private WatchedFolder? StartWatching(IndexedFolder folder)
    {
        try
        {
            var watcher = factory!.Watch(folder.ToHandle());
            var entry = new WatchedFolder(folder, watcher);
            watcher.Changed += (_, change) => OnChanged(entry, change);
            watcher.Failed += (_, error) => OnFailed(entry, error);
            logger.LogInformation("Watching {Folder}", folder.DisplayPath);
            return entry;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            // E.g. the folder went away since its last check. The check marks it Offline, and coming back watches it. A
            // folder that's still Online but can't be watched isn't tried again until the next refresh.
            logger.LogWarning(exception, "Can't watch {Folder}", folder.DisplayPath);
            Track(Task.Run(() => CheckAvailabilityAsync(folder.Id)));
            return null;
        }
    }

    /// <summary>
    /// Recovers the events a watcher lost. An overflow leaves the watcher running, so the folder is rescanned. Any other
    /// error stops the watcher; the folder's availability is checked, and if it's still Online it's watched again and
    /// rescanned. If it's Offline, coming back Online watches it again.
    /// </summary>
    private void OnFailed(WatchedFolder entry, FolderWatchErrorEventArgs error)
    {
        if (entry.IsStopped)
        {
            return;
        }

        var folder = entry.Folder;
        if (error.IsOverflow)
        {
            logger.LogWarning("The watcher of {Folder} lost events; rescanning it", folder.DisplayPath);
            RequestRescan(folder.Id);
            return;
        }

        DateTimeOffset retryAt;
        lock (syncRoot)
        {
            if (disposed || !watched.TryGetValue(folder.Id, out var current) || !ReferenceEquals(current, entry))
            {
                return;
            }

            entry.Stop();
            watched.Remove(folder.Id);
            buffer.Forget(folder.Id);
            retryAt = CountFailure(folder.Id);
        }

        PublishMonitoringChanged();
        StateChanged?.Invoke(this, EventArgs.Empty);

        logger.LogWarning(error.Exception, "The watcher of {Folder} failed; checking the folder", folder.DisplayPath);
        Track(RecoverAsync(folder.Id, retryAt));
    }

    /// <summary>
    /// Counts a watcher failure and tells when the folder may be watched again: at once after a first failure, or after
    /// one following a restart that didn't last <see cref="MaxFailureWait"/>, a wait doubling from
    /// <see cref="FirstFailureWait"/>.
    /// </summary>
    // Called under the lock.
    private DateTimeOffset CountFailure(Id<IndexedFolder> folderId)
    {
        var now = clock.GetUtcNow();
        var count = failures.TryGetValue(folderId, out var last) && now - last.RetryAt < MaxFailureWait ? last.Count + 1 : 1;
        var wait = count == 1
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Math.Min(MaxFailureWait.Ticks, FirstFailureWait.Ticks << Math.Min(count - 2, 10)));
        var retryAt = now + wait;
        failures[folderId] = new Failures(count, retryAt);
        return retryAt;
    }

    private async Task RecoverAsync(Id<IndexedFolder> folderId, DateTimeOffset retryAt)
    {
        await CheckAvailabilityAsync(folderId).ConfigureAwait(false);
        var wait = retryAt - clock.GetUtcNow();
        if (wait > TimeSpan.Zero)
        {
            logger.LogWarning("Folder {Folder} is watched again in {Wait} after repeated watcher failures", folderId, wait);
            try
            {
                await Task.Delay(wait, clock, stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        await RefreshAsync().ConfigureAwait(false);
        if (IsWatched(folderId))
        {
            RequestRescan(folderId);
        }
    }

    /// <summary>
    /// Checks a folder's availability.
    /// </summary>
    /// <returns>Whether the check changed the folder's status or relinked it.</returns>
    private async Task<bool> CheckAvailabilityAsync(Id<IndexedFolder> folderId)
    {
        var token = stopping.Token;
        try
        {
            var changed = await sender.Send(new CheckFolderAvailabilityCommand([folderId]), token).ConfigureAwait(false);
            return changed.Any(folder => folder.Id == folderId);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            // E.g. the library closed during the check.
            logger.LogWarning(exception, "Couldn't check folder {Folder} after its watcher failed", folderId);
        }

        return false;
    }

    /// <summary>
    /// Checks a folder whose changes were dropped because its root couldn't be reached. A folder the check finds Online
    /// changes no status, so no catch-up runs for it, and it's rescanned to find the dropped changes (MFC-1047). One the
    /// check made Offline is caught up when it's back, and one it relinked or brought back Online is caught up now.
    /// </summary>
    private async Task CheckThenRescanAsync(Id<IndexedFolder> folderId)
    {
        if (await CheckAvailabilityAsync(folderId).ConfigureAwait(false))
        {
            return;
        }

        await RefreshAsync().ConfigureAwait(false);
        if (IsWatched(folderId))
        {
            logger.LogInformation("Watched folder {Folder} is reachable again; rescanning it for its dropped changes", folderId);
            RequestRescan(folderId);
        }
    }

    /// <summary>
    /// Asks for a rescan of the folder, unless one is asked for already.
    /// </summary>
    private void RequestRescan(Id<IndexedFolder> folderId)
    {
        lock (syncRoot)
        {
            if (disposed || !rescansWanted.Add(folderId))
            {
                return;
            }
        }

        Track(RescanAsync(folderId));
    }

    /// <summary>
    /// Starts a rescan of the folder. While the folder's import or rescan work runs, it's asked for again every
    /// <see cref="BusyRetry"/>, because the running job may have passed the changed files; it stops when the folder isn't
    /// watched anymore.
    /// </summary>
    private async Task RescanAsync(Id<IndexedFolder> folderId)
    {
        var token = stopping.Token;
        try
        {
            while (IsWatched(folderId))
            {
                if (pause is not null)
                {
                    await pause.WaitWhilePausedAsync(token).ConfigureAwait(false);
                    if (!IsWatched(folderId))
                    {
                        return;
                    }
                }

                var result = await sender.Send(new StartRescanCommand(folderId), token).ConfigureAwait(false);
                if (result.Outcome != StartRescanOutcome.Busy)
                {
                    if (result.Outcome != StartRescanOutcome.Started)
                    {
                        logger.LogWarning("Folder {Folder} wasn't rescanned after lost events: {Outcome}", folderId, result.Outcome);
                    }

                    return;
                }

                await Task.Delay(BusyRetry, clock, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            // E.g. the library closed. Opening it again checks its folders.
            logger.LogWarning(exception, "Couldn't rescan folder {Folder} after lost events", folderId);
        }
        finally
        {
            lock (syncRoot)
            {
                rescansWanted.Remove(folderId);
            }
        }
    }

    private bool IsWatched(Id<IndexedFolder> folderId)
    {
        lock (syncRoot)
        {
            return !disposed && watched.ContainsKey(folderId);
        }
    }

    /// <summary>
    /// Keeps a recovery task until it's done, for <see cref="WaitForRecoveryAsync"/>. The tasks don't fail.
    /// </summary>
    private void Track(Task task)
    {
        lock (syncRoot)
        {
            recoveries.Add(task);
        }

        _ = task.ContinueWith(
            done =>
            {
                lock (syncRoot)
                {
                    recoveries.Remove(done);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnChanged(WatchedFolder folder, FolderWatchEvent change)
    {
        if (folder.IsStopped)
        {
            return;
        }

        if (Filter(folder.Folder, change) is { } kept)
        {
            buffer.Record(folder.Folder.Id, folder.Handle, kept);
        }
    }

    /// <summary>
    /// Drops what the ignore list, the folder's rules or the supported formats leave out. A rename between a path that's
    /// left out and one that isn't is a new file or a deletion.
    /// </summary>
    /// <remarks>
    /// A rename whose path was gone when it was seen, between names that aren't media files, is kept as a folder rename
    /// (MFC-1032): a folder renamed twice at once is gone by its first event, and the buffer joins the two renames.
    /// </remarks>
    private FolderWatchEvent? Filter(IndexedFolder folder, FolderWatchEvent change)
    {
        var included = Includes(folder, change.RelativePath, change.IsDirectory, isDeletion: change.Kind == FolderWatchEventKind.Deleted);
        if (change is not { Kind: FolderWatchEventKind.Renamed, OldRelativePath: { } oldPath })
        {
            return included ? change : null;
        }

        var result = (Includes(folder, oldPath, change.IsDirectory), included) switch
        {
            (true, true) => change,
            (false, true) => change with { Kind = FolderWatchEventKind.Created, OldRelativePath = null },
            (true, false) => new FolderWatchEvent(FolderWatchEventKind.Deleted, oldPath),
            _ => null,
        };
        if (result is null && change is { IsGone: true, IsDirectory: false })
        {
            var asFolder = change with { IsDirectory = true, IsGone = false };
            return Filter(folder, asFolder) is { Kind: FolderWatchEventKind.Renamed } kept ? kept : null;
        }

        return result;
    }

    /// <summary>
    /// Checks a path against the ignore list, the folder's rules and the supported formats. A deletion's path may be a
    /// folder (the watcher can't tell once it's gone), so it's kept if it would be included as a file or entered as a
    /// folder, and it isn't checked for a media extension.
    /// </summary>
    private bool Includes(IndexedFolder folder, string path, bool isDirectory, bool isDeletion = false)
    {
        if (ignore.IsIgnored(path))
        {
            return false;
        }

        // A partial folder's rules don't apply (D012); its tracked files are checked when the change is ready.
        if (!folder.SelectedFilesOnly)
        {
            var rules = new FolderRuleMatcher(folder.Rules);
            var allowed = isDirectory ? rules.ShouldEnterFolder(path)
                : isDeletion ? rules.IsIncluded(path) || rules.ShouldEnterFolder(path)
                : rules.IsIncluded(path);
            if (!allowed)
            {
                return false;
            }

            if (!folder.Rules.IncludeSubfolders && path.Contains('/', StringComparison.Ordinal))
            {
                return false;
            }
        }

        return isDirectory || isDeletion || formats.IsCandidate(path);
    }

    /// <summary>
    /// Tells the buffer whether a file found in a new folder is worth checking.
    /// </summary>
    private bool IncludesFile(Id<IndexedFolder> folderId, string path)
    {
        IndexedFolder folder;
        lock (syncRoot)
        {
            if (!watched.TryGetValue(folderId, out var entry))
            {
                return false;
            }

            folder = entry.Folder;
        }

        return Includes(folder, path, isDirectory: false);
    }

    /// <summary>
    /// Filters a ready change like a raw event: the files of a new or renamed folder were never filtered one by one.
    /// </summary>
    private FolderChange? Filter(IndexedFolder folder, FolderChange change)
    {
        var isDeletion = change.Kind == FolderChangeKind.Deleted;
        var included = Includes(folder, change.RelativePath, isDirectory: false, isDeletion);
        if (change is not { Kind: FolderChangeKind.Renamed, OldRelativePath: { } oldPath })
        {
            return included ? change : null;
        }

        return (Includes(folder, oldPath, isDirectory: false), included) switch
        {
            (true, true) => change,
            (false, true) => change with { Kind = FolderChangeKind.Changed, OldRelativePath = null },
            (true, false) => new FolderChange(FolderChangeKind.Deleted, oldPath),
            _ => null,
        };
    }

    /// <summary>
    /// Keeps the ready changes that the library should hear about: hidden files and cloud placeholders only where the
    /// rules include them, a partial folder's tracked files only, and nothing about excluded items.
    /// </summary>
    private async Task<IReadOnlyList<FolderChange>> KeepAsync(IndexedFolder folder, IOpenLibrary current, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        var kept = new List<FolderChange>(changes.Count);
        foreach (var ready in changes)
        {
            if (Filter(folder, ready) is not { } change)
            {
                continue;
            }

            if (change.Stat is { } stat
                && ((stat.IsHidden && !folder.Rules.IncludeHidden) || (stat.IsCloudPlaceholder && !folder.Rules.IncludeCloudPlaceholders)))
            {
                if (change is { Kind: FolderChangeKind.Renamed, OldRelativePath: { } hiddenFrom })
                {
                    kept.Add(new FolderChange(FolderChangeKind.Deleted, hiddenFrom));
                }

                continue;
            }

            var item = await FindAsync(current, folder.Id, change.OldRelativePath ?? change.RelativePath, cancellationToken).ConfigureAwait(false);
            if (folder.SelectedFilesOnly && item is null && (change.OldRelativePath is null
                || await FindAsync(current, folder.Id, change.RelativePath, cancellationToken).ConfigureAwait(false) is null))
            {
                continue;
            }

            if (item?.Status == MediaItemStatus.Excluded)
            {
                continue;
            }

            kept.Add(change);
        }

        return kept;
    }

    private async Task PublishAsync(Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        IndexedFolder folder;
        IOpenLibrary current;
        lock (syncRoot)
        {
            // Stopped being watched during the checks, e.g. it went Offline: its changes are found when it's checked again.
            if (!watched.TryGetValue(folderId, out var entry) || library is null)
            {
                return;
            }

            folder = entry.Folder;
            current = library;
        }

        changes = await KeepAsync(folder, current, changes, cancellationToken).ConfigureAwait(false);
        if (changes.Count == 0)
        {
            return;
        }

        // A file on a drive just unplugged looks deleted. Its folder is checked instead, which marks it Offline, so no
        // item is lost (E04-S04-T02); its changes are found by the catch-up when it's back, or by a rescan if the check
        // finds it Online after all (MFC-1047).
        if (changes.Any(change => change.Kind == FolderChangeKind.Deleted)
            && !await files.IsAvailableAsync(folder.ToHandle(), cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("Watched folder {Folder} can't be reached: its {Count} changes are dropped and it's checked", folderId, changes.Count);
            Track(CheckThenRescanAsync(folderId));
            return;
        }

        logger.LogInformation("{Count} changes in watched folder {Folder}", changes.Count, folderId);
        await publisher.Publish(new FolderChangesDetected(folderId, changes), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// How often a folder's watcher failed in a row, and when it may be watched again.
    /// </summary>
    private sealed record Failures(int Count, DateTimeOffset RetryAt);

    /// <summary>
    /// A watched folder and its watcher.
    /// </summary>
    private sealed class WatchedFolder(IndexedFolder folder, IFolderWatcher watcher)
    {
        private volatile IndexedFolder folder = folder;
        private volatile bool stopped;

        /// <summary>
        /// Gets or sets the folder as last read, with its current rules.
        /// </summary>
        public IndexedFolder Folder
        {
            get => folder;
            set => folder = value;
        }

        /// <summary>
        /// Gets the root the watcher was started on.
        /// </summary>
        public string Root { get; } = folder.PlatformHandle;

        /// <summary>
        /// Gets the handle the watcher was started on.
        /// </summary>
        public FolderHandle Handle { get; } = folder.ToHandle();

        public bool IsStopped => stopped;

        public void Stop()
        {
            stopped = true;
            watcher.Dispose();
        }
    }
}
