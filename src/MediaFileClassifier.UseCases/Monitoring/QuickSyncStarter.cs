using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Rescan;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Catches up with the changes made while the app was closed (E04-S03): when a library opens, it queues one quick sync
/// of the library's Online folders, a background job that shows in the processing queue and waits while processing is
/// paused. It starts only once the folder watchers run (<see cref="FolderWatchManager.EnsureWatchingAsync"/>), so a file
/// that changes while the sync lists its folder is seen by the watcher as well; both compare by path, so nothing is
/// imported twice. A library whose quick sync is turned off is left alone. While import, rescan or removal work is
/// unfinished, e.g. resumed from the last session, the start is asked for again every <see cref="BusyRetry"/>.
/// </summary>
public sealed class QuickSyncStarter : IDisposable
{
    /// <summary>
    /// How long to wait before asking again for a quick sync that was refused because other work is running.
    /// </summary>
    public static readonly TimeSpan BusyRetry = FolderWatchManager.BusyRetry;

    private readonly ILibrarySession session;
    private readonly FolderWatchManager watchers;
    private readonly ISender sender;
    private readonly ISettingsStore settings;
    private readonly TimeProvider clock;
    private readonly ILogger<QuickSyncStarter> logger;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Cancelled by Dispose; a run already under way may still read its token. It has no timer or wait handle.")]
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock syncRoot = new();
    private IOpenLibrary? handled;
    private Task running = Task.CompletedTask;
    private bool started;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuickSyncStarter"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="watchers">The folder watchers, which must run before the sync starts.</param>
    /// <param name="sender">Starts the sync.</param>
    /// <param name="settings">The settings, with the library's switch.</param>
    /// <param name="clock">Times the retries.</param>
    /// <param name="logger">Logger.</param>
    public QuickSyncStarter(
        ILibrarySession session,
        FolderWatchManager watchers,
        ISender sender,
        ISettingsStore settings,
        TimeProvider clock,
        ILogger<QuickSyncStarter> logger)
    {
        this.session = session;
        this.watchers = watchers;
        this.sender = sender;
        this.settings = settings;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// Starts following the open library: syncs the one that's open now, and each one opened later. Calling it again
    /// does nothing.
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
        }

        OnLibraryChanged(this, EventArgs.Empty);
    }

    /// <summary>
    /// Waits until the sync of the library opened last has been queued, or given up.
    /// </summary>
    /// <returns>A task that completes when the start is done.</returns>
    public Task WaitAsync()
    {
        lock (syncRoot)
        {
            return running;
        }
    }

    /// <summary>
    /// Stops following the library.
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
        }

        stopping.Cancel();
    }

    private void OnLibraryChanged(object? source, EventArgs e)
    {
        lock (syncRoot)
        {
            var library = session.Current;
            if (disposed || library is null || ReferenceEquals(library, handled))
            {
                return;
            }

            handled = library;
            running = Task.Run(() => RunAsync(library));
        }
    }

    private bool IsOn()
    {
        if (settings.Get(KnownSettings.QuickSyncOnOpen))
        {
            return true;
        }

        logger.LogInformation("The quick sync on open is turned off for this library");
        return false;
    }

    private static async Task<bool> LibraryWideSyncUnfinishedAsync(IOpenLibrary library, CancellationToken cancellationToken) =>
        (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false)).Any(job =>
            job is { FolderId: null, Type: RescanStages.RescanJob or RescanStages.QuickSyncJob }

            // Waiting for a model means its listing is done, as for StartRescanCommandHandler.
            && job.State != ProcessingJobState.WaitingForModel);

    // The refresh doesn't tell when it couldn't watch a folder (it failed, or the folder waits after repeated failures);
    // the sync goes on, but the gap is worth a line in the log.
    private async Task WarnIfNotWatchedAsync(IOpenLibrary library, CancellationToken cancellationToken)
    {
        if (!watchers.IsWatching)
        {
            return;
        }

        var watched = watchers.WatchedFolders;
        var unwatched = (await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false))
            .Count(folder => folder.Status == FolderStatus.Online && !watched.Contains(folder.Id));
        if (unwatched > 0)
        {
            logger.LogWarning("The quick sync on open starts while {Count} Online folder(s) aren't watched", unwatched);
        }
    }

    private async Task RunAsync(IOpenLibrary library)
    {
        var token = stopping.Token;
        try
        {
            if (!IsOn())
            {
                return;
            }

            await watchers.EnsureWatchingAsync().ConfigureAwait(false);
            await WarnIfNotWatchedAsync(library, token).ConfigureAwait(false);
            while (ReferenceEquals(session.Current, library))
            {
                // Read again each time: the user may turn the switch off while the start waits.
                if (!IsOn())
                {
                    return;
                }

                var result = await sender.Send(new StartRescanCommand(Quick: true), token).ConfigureAwait(false);
                if (result.Outcome != StartRescanOutcome.Busy)
                {
                    if (result.Outcome != StartRescanOutcome.Started)
                    {
                        logger.LogInformation("No quick sync on open: {Outcome}", result.Outcome);
                    }

                    return;
                }

                // A library-wide rescan or sync that's unfinished lists every folder itself, e.g. one resumed from the
                // last session or a manual rescan: queueing another when it ends would list everything twice.
                if (await LibraryWideSyncUnfinishedAsync(library, token).ConfigureAwait(false))
                {
                    logger.LogInformation("No quick sync on open: a library rescan is unfinished");
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
            logger.LogWarning(exception, "Couldn't start the quick sync on open");
        }
    }
}
