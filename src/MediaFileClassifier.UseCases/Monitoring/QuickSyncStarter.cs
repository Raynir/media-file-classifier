using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
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

    private async Task RunAsync(IOpenLibrary library)
    {
        var token = stopping.Token;
        try
        {
            if (!settings.Get(MonitoringSettings.QuickSyncOnOpen))
            {
                logger.LogInformation("The quick sync on open is turned off for this library");
                return;
            }

            await watchers.EnsureWatchingAsync().ConfigureAwait(false);
            while (ReferenceEquals(session.Current, library))
            {
                var result = await sender.Send(new StartRescanCommand(Quick: true), token).ConfigureAwait(false);
                if (result.Outcome != StartRescanOutcome.Busy)
                {
                    if (result.Outcome != StartRescanOutcome.Started)
                    {
                        logger.LogInformation("No quick sync on open: {Outcome}", result.Outcome);
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
            logger.LogWarning(exception, "Couldn't start the quick sync on open");
        }
    }
}
