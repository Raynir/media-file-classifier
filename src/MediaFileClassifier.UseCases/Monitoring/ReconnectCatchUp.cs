using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Catches up with what changed on folders while their drive was away (E04-S04-T02): each folder that comes back Online
/// gets the quick sync of E04-S03 (<see cref="StartRescanCommand"/> with <c>Quick</c>), whatever the library's "quick
/// sync on open" switch says, unless the folder is in Manual mode (E04-S05-T01). The sync starts once the folder watchers run, so a file that changes while it lists is seen
/// by the watcher as well. A start refused because the folder's import or rescan work is running is asked for again every
/// <see cref="BusyRetry"/> while the folder is still Online, its library still open and, where folders are watched and it
/// runs in Watch mode, the folder still watched (as <see cref="FolderWatchManager"/> does for lost events, D517).
/// </summary>
public sealed class ReconnectCatchUp : IDisposable
{
    /// <summary>
    /// How long to wait before asking again for a sync that was refused because other work of the folder is running.
    /// </summary>
    public static readonly TimeSpan BusyRetry = FolderWatchManager.BusyRetry;

    private readonly ILibrarySession session;
    private readonly FolderWatchManager watchers;
    private readonly FolderLocations? locations;
    private readonly ISender sender;
    private readonly TimeProvider clock;
    private readonly ILogger<ReconnectCatchUp> logger;
    private readonly MonitoringPause? pause;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Cancelled by Dispose; a sync already under way may still read its token. It has no timer or wait handle.")]
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock syncRoot = new();
    private readonly HashSet<Id<IndexedFolder>> pending = [];
    private readonly HashSet<Task> running = [];
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReconnectCatchUp"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="watchers">The folder watchers, which must run before a sync starts.</param>
    /// <param name="sender">Starts the syncs.</param>
    /// <param name="clock">Times the retries.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="pause">Holds the catch-ups while monitoring is paused (E04-S06-T02), or <see langword="null"/> for no pause.</param>
    /// <param name="locations">
    /// Tells the mode each folder runs in: a polled folder isn't watched, and is still caught up. Without it every folder
    /// counts as watched.
    /// </param>
    public ReconnectCatchUp(
        ILibrarySession session,
        FolderWatchManager watchers,
        ISender sender,
        TimeProvider clock,
        ILogger<ReconnectCatchUp> logger,
        MonitoringPause? pause = null,
        FolderLocations? locations = null)
    {
        this.pause = pause;
        this.session = session;
        this.watchers = watchers;
        this.locations = locations;
        this.sender = sender;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// Starts the catch-up of folders of the open library that came back Online. It runs in the background; a folder
    /// whose catch-up is still waiting isn't asked for twice.
    /// </summary>
    /// <param name="folderIds">The folders that came back.</param>
    public void Start(IEnumerable<Id<IndexedFolder>> folderIds)
    {
        var library = session.Current;
        if (library is null)
        {
            return;
        }

        foreach (var folderId in folderIds)
        {
            lock (syncRoot)
            {
                if (disposed || !pending.Add(folderId))
                {
                    continue;
                }
            }

            Track(Task.Run(() => RunAsync(library, folderId)));
        }
    }

    /// <summary>
    /// Waits until every catch-up started so far has been queued or given up.
    /// </summary>
    /// <returns>A task that completes when they're done.</returns>
    public Task WaitAsync()
    {
        lock (syncRoot)
        {
            return Task.WhenAll([.. running]);
        }
    }

    /// <summary>
    /// Stops the catch-ups that wait.
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
        }

        stopping.Cancel();
    }

    private void Track(Task task)
    {
        lock (syncRoot)
        {
            running.Add(task);
        }

        _ = task.ContinueWith(
            done =>
            {
                lock (syncRoot)
                {
                    running.Remove(done);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<bool> StillWantedAsync(IOpenLibrary library, Id<IndexedFolder> folderId, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(session.Current, library))
        {
            return false;
        }

        var folders = await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false);
        if (folders.FirstOrDefault(folder => folder.Id == folderId) is not { Status: FolderStatus.Online } folder
            || folder.MonitoringMode == MonitoringMode.Manual)
        {
            return false;
        }

        // Where folders are watched, an Online folder in Watch mode that isn't watched has lost its watcher or is being
        // removed: the watcher's own recovery rescans it. A polled folder is never watched (E04-S05).
        if (watchers.IsWatching && !watchers.WatchedFolders.Contains(folderId))
        {
            var mode = locations is null
                ? MonitoringMode.Watch
                : await locations.GetEffectiveModeAsync(folder, cancellationToken).ConfigureAwait(false);
            return mode != MonitoringMode.Watch;
        }

        return true;
    }

    private async Task RunAsync(IOpenLibrary library, Id<IndexedFolder> folderId)
    {
        var token = stopping.Token;
        try
        {
            // The folder's watcher restarts through the repository's StatusChanged; wait for it so nothing changed during
            // the listing is lost.
            await watchers.EnsureWatchingAsync().ConfigureAwait(false);
            while (await StillWantedAsync(library, folderId, token).ConfigureAwait(false))
            {
                if (pause is not null)
                {
                    await pause.WaitWhilePausedAsync(token).ConfigureAwait(false);
                    if (!await StillWantedAsync(library, folderId, token).ConfigureAwait(false))
                    {
                        return;
                    }
                }

                var result = await sender.Send(new StartRescanCommand(folderId, Quick: true), token).ConfigureAwait(false);
                if (result.Outcome != StartRescanOutcome.Busy)
                {
                    if (result.Outcome != StartRescanOutcome.Started)
                    {
                        logger.LogInformation("No catch-up of reconnected folder {Folder}: {Outcome}", folderId, result.Outcome);
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
            // E.g. the library closed. Opening it again syncs it.
            logger.LogWarning(exception, "Couldn't catch up reconnected folder {Folder}", folderId);
        }
        finally
        {
            lock (syncRoot)
            {
                pending.Remove(folderId);
            }
        }
    }
}
