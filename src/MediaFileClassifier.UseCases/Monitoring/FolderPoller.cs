using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Polls the open library's Online folders that run in Poll mode (E04-S05-T02; see <see cref="FolderLocations"/>): every
/// <see cref="MonitoringSettings.PollIntervalMinutes"/> each one gets the quick sync of E04-S03
/// (<see cref="StartRescanCommand"/> with <c>Quick</c>), a background job whose summary shows only when something changed.
/// A folder's first poll comes one interval after it starts being polled, because opening the library and reconnecting a
/// drive sync it already. A poll refused because the folder's import, rescan or removal work is unfinished waits for the
/// next interval: that work lists the folder itself. Polls follow the library opening and closing, folders being added,
/// changed or removed, going Offline and coming back, and the interval changing. Nothing is polled while monitoring is
/// off for the library (<see cref="KnownSettings.MonitoringEnabled"/>); while monitoring is paused, a due poll waits and
/// runs once on resume (E04-S05, like the watchers). Pausing all processing holds the quick syncs already queued (D536).
/// </summary>
public sealed class FolderPoller : IDisposable
{
    private readonly ILibrarySession session;
    private readonly FolderLocations locations;
    private readonly ISender sender;
    private readonly IPublisher publisher;
    private readonly ISettingsStore settings;
    private readonly TimeProvider clock;
    private readonly ILogger<FolderPoller> logger;
    private readonly MonitoringPause? pause;
    private readonly Lock syncRoot = new();
    private readonly Dictionary<Id<IndexedFolder>, DateTimeOffset> due = [];
    private readonly ITimer timer;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A refresh already under way may still use it after Dispose; it holds no OS handle unless its wait handle is read.")]
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Cancelled by Dispose; a poll already under way may still read its token. It has no timer or wait handle.")]
    private readonly CancellationTokenSource stopping = new();

    private IOpenLibrary? library;
    private IFolderRepository? folderEvents;
    private Task polling = Task.CompletedTask;
    private bool started;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderPoller"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="locations">Tells which folders run in Poll mode.</param>
    /// <param name="sender">Starts the quick syncs.</param>
    /// <param name="publisher">Tells when the polled folders change.</param>
    /// <param name="settings">Holds the poll interval.</param>
    /// <param name="clock">Times the polls.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="pause">Pauses monitoring for the whole app, or <see langword="null"/> for no pause.</param>
    public FolderPoller(
        ILibrarySession session,
        FolderLocations locations,
        ISender sender,
        IPublisher publisher,
        ISettingsStore settings,
        TimeProvider clock,
        ILogger<FolderPoller> logger,
        MonitoringPause? pause = null)
    {
        this.session = session;
        this.locations = locations;
        this.sender = sender;
        this.publisher = publisher;
        this.settings = settings;
        this.clock = clock;
        this.logger = logger;
        this.pause = pause;
        timer = clock.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Gets the time between two polls of a folder.
    /// </summary>
    public TimeSpan Interval => TimeSpan.FromMinutes(Math.Clamp(
        settings.Get(MonitoringSettings.PollIntervalMinutes),
        MonitoringSettings.PollIntervalMinutes.Minimum,
        MonitoringSettings.PollIntervalMinutes.Maximum));

    /// <summary>
    /// Gets the folders polled now.
    /// </summary>
    public IReadOnlyCollection<Id<IndexedFolder>> PolledFolders
    {
        get
        {
            lock (syncRoot)
            {
                return [.. due.Keys];
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the poller follows the open library.
    /// </summary>
    public bool IsPolling
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
    /// Gets when a folder is polled next, or <see langword="null"/> when it isn't polled.
    /// </summary>
    /// <param name="folderId">The folder.</param>
    /// <returns>The time of its next poll.</returns>
    public DateTimeOffset? NextPollOf(Id<IndexedFolder> folderId)
    {
        lock (syncRoot)
        {
            return due.TryGetValue(folderId, out var next) ? next : null;
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
            if (started)
            {
                return;
            }

            started = true;
            session.CurrentChanged += OnLibraryChanged;
            settings.Changed += OnSettingChanged;
        }

        RequestRefresh();
    }

    /// <summary>
    /// Brings the polled folders in line with the open library's Online folders in Poll mode now. Refreshes run one at a
    /// time; the folder list is read inside the refresh, so a change that raised an event while one ran is seen by the next.
    /// </summary>
    /// <returns>A task that completes when the polled folders match.</returns>
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
            var changed = Follow(current);
            if (current is not null && settings.Get(KnownSettings.MonitoringEnabled))
            {
                var polled = new List<Id<IndexedFolder>>();
                foreach (var folder in await IndexedFolders.GetActiveAsync(current, token).ConfigureAwait(false))
                {
                    if (folder.Status == FolderStatus.Online
                        && await locations.GetEffectiveModeAsync(folder, token).ConfigureAwait(false) == MonitoringMode.Poll)
                    {
                        polled.Add(folder.Id);
                    }
                }

                changed |= Apply(current, polled);
            }
            else if (current is not null)
            {
                // Monitoring is off for the library: no folder is polled.
                changed |= Apply(current, []);
            }

            if (changed)
            {
                PublishChanged();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            // E.g. the library closed during the read. Its closing raises a refresh of its own.
            logger.LogWarning(exception, "Couldn't update the polled folders");
        }
        finally
        {
            refreshGate.Release();
        }
    }

    /// <summary>
    /// Waits until the polls under way are queued or given up.
    /// </summary>
    /// <returns>A task that completes when no poll is under way.</returns>
    public Task WaitForPollsAsync()
    {
        lock (syncRoot)
        {
            return polling;
        }
    }

    /// <summary>
    /// Stops polling and stops following the library.
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
        }

        Follow(null);
        timer.Dispose();
        stopping.Cancel();
    }

    private void RequestRefresh() => _ = RefreshAsync();

    private void OnLibraryChanged(object? sender, EventArgs e) => RequestRefresh();

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Setting.Key == KnownSettings.MonitoringEnabled.Key)
        {
            RequestRefresh();
            return;
        }

        if (e.Setting.Key != MonitoringSettings.PollIntervalMinutes.Key)
        {
            return;
        }

        // A shorter interval applies at once; a longer one from the next poll on.
        lock (syncRoot)
        {
            var latest = clock.GetUtcNow() + Interval;
            foreach (var folderId in due.Keys.ToList())
            {
                if (due[folderId] > latest)
                {
                    due[folderId] = latest;
                }
            }

            Reschedule();
        }

        PublishChanged();
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
    /// Follows the folder events of the open library. When the library changed, the old one's folders stop being polled.
    /// </summary>
    /// <returns><see langword="true"/> when folders stopped being polled.</returns>
    private bool Follow(IOpenLibrary? current)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                current = null;
            }

            if (ReferenceEquals(current, library))
            {
                return false;
            }

            if (folderEvents is not null)
            {
                folderEvents.Changed -= OnFoldersChanged;
                folderEvents.StatusChanged -= OnFoldersChanged;
            }

            var stopped = due.Count > 0;
            due.Clear();
            library = current;
            folderEvents = current?.Folders;
            if (folderEvents is not null)
            {
                folderEvents.Changed += OnFoldersChanged;
                folderEvents.StatusChanged += OnFoldersChanged;
            }

            Reschedule();
            return stopped;
        }
    }

    // Returns whether the set of polled folders changed.
    private bool Apply(IOpenLibrary current, IReadOnlyList<Id<IndexedFolder>> polled)
    {
        lock (syncRoot)
        {
            // Another library opened during the read; its own refresh is queued.
            if (disposed || !ReferenceEquals(current, library))
            {
                return false;
            }

            var wanted = polled.ToHashSet();
            var changed = false;
            foreach (var folderId in due.Keys.Where(folderId => !wanted.Contains(folderId)).ToList())
            {
                due.Remove(folderId);
                changed = true;
                logger.LogInformation("Stopped polling folder {Folder}", folderId);
            }

            var first = clock.GetUtcNow() + Interval;
            foreach (var folderId in wanted.Where(folderId => !due.ContainsKey(folderId)))
            {
                due[folderId] = first;
                changed = true;
                logger.LogInformation("Polling folder {Folder} every {Interval}", folderId, Interval);
            }

            Reschedule();
            return changed;
        }
    }

    /// <summary>
    /// Sets the timer to the next poll. Called under the lock.
    /// </summary>
    private void Reschedule()
    {
        if (disposed)
        {
            return;
        }

        if (due.Count == 0)
        {
            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var wait = due.Values.Min() - clock.GetUtcNow();
        timer.Change(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private void OnTimer()
    {
        lock (syncRoot)
        {
            // One round of polls at a time; the round that runs reschedules when it ends.
            if (disposed || !polling.IsCompleted)
            {
                return;
            }

            polling = Task.Run(PollDueAsync);
        }
    }

    private async Task PollDueAsync()
    {
        var token = stopping.Token;
        List<Id<IndexedFolder>> folders = [];
        try
        {
            // While monitoring is paused the due polls wait, and run once on resume. The timer stays off meanwhile: this
            // round is under way, and it reschedules when it ends.
            await WaitWhilePausedAsync(token).ConfigureAwait(false);

            // Read after the wait: folders that stopped being polled meanwhile, e.g. monitoring was turned off, are gone.
            lock (syncRoot)
            {
                var now = clock.GetUtcNow();
                var next = now + Interval;
                folders = [.. due.Where(entry => entry.Value <= now).Select(entry => entry.Key)];
                foreach (var folderId in folders)
                {
                    due[folderId] = next;
                }
            }

            foreach (var folderId in folders)
            {
                // Paused during the round: the rest of it waits too.
                await WaitWhilePausedAsync(token).ConfigureAwait(false);
                lock (syncRoot)
                {
                    // Stopped being polled while the round waited, e.g. monitoring was turned off.
                    if (!due.ContainsKey(folderId))
                    {
                        continue;
                    }
                }

                await PollAsync(folderId, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            lock (syncRoot)
            {
                Reschedule();
            }

            // Their next poll moved.
            if (folders.Count > 0)
            {
                PublishChanged();
            }
        }
    }

    /// <summary>
    /// Waits while monitoring is paused. Paused again right after a resume, it waits again.
    /// </summary>
    private async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        while (pause?.IsPaused == true)
        {
            await pause.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PollAsync(Id<IndexedFolder> folderId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await sender.Send(new StartRescanCommand(folderId, Quick: true), cancellationToken).ConfigureAwait(false);
            if (result.Outcome == StartRescanOutcome.Started)
            {
                logger.LogInformation("Polling folder {Folder}: quick sync queued", folderId);
            }
            else
            {
                logger.LogInformation("Polling folder {Folder}: no quick sync ({Outcome}); next try in {Interval}", folderId, result.Outcome, Interval);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // E.g. the library closed. The next poll, or opening it again, syncs it.
            logger.LogWarning(exception, "Couldn't poll folder {Folder}", folderId);
        }
    }

    private void PublishChanged() => _ = Task.Run(async () =>
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
            logger.LogWarning(exception, "Couldn't report the change of the polled folders");
        }
    });
}
