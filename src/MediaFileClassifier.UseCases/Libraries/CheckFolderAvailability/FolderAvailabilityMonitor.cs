using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;

/// <summary>
/// Keeps the open library's folder statuses live (E14-S07-T04, DNEW-4, amends D188). When a volume arrives or goes
/// away, every folder is checked once the events have been quiet for <see cref="Debounce"/>; and every
/// <see cref="PollInterval"/> the Offline folders alone are checked, because a network share coming back raises no
/// volume event. Online folders aren't polled. Checks run one at a time; a poll that finds a check running is skipped,
/// and a volume check cancels a running poll, which may be stuck on an unreachable network share, rather than wait for it.
/// </summary>
public sealed class FolderAvailabilityMonitor : IDisposable
{
    /// <summary>
    /// How long volume events must be quiet before the folders are checked: plugging in a drive raises several.
    /// </summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often the Offline folders are checked.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IVolumeWatcher volumes;
    private readonly ISender sender;
    private readonly TimeProvider clock;
    private readonly ILogger<FolderAvailabilityMonitor> logger;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A timer callback already under way may still use it after Dispose; it holds no OS handle unless its wait handle is read.")]
    private readonly SemaphoreSlim gate = new(1, 1);

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Cancelled by Dispose; a timer callback already under way may still read its token. It has no timer or wait handle.")]
    private readonly CancellationTokenSource stopping = new();

    private readonly Lock syncRoot = new();
    private ITimer? debounce;
    private ITimer? poll;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Owned and disposed by the running poll itself; Dispose cancels it through the linked stopping token.")]
    private CancellationTokenSource? runningPoll;
    private int waitingVolumeChecks;
    private bool started;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderAvailabilityMonitor"/> class.
    /// </summary>
    /// <param name="volumes">Tells when a volume arrived or went away.</param>
    /// <param name="sender">Sends the availability checks.</param>
    /// <param name="clock">Runs the debounce and the poll.</param>
    /// <param name="logger">Logger.</param>
    public FolderAvailabilityMonitor(IVolumeWatcher volumes, ISender sender, TimeProvider clock, ILogger<FolderAvailabilityMonitor> logger)
    {
        this.volumes = volumes;
        this.sender = sender;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// Starts listening for volume events and polling. Calling it again does nothing.
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
            debounce = clock.CreateTimer(_ => _ = CheckAsync(offlineOnly: false), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            poll = clock.CreateTimer(_ => _ = CheckAsync(offlineOnly: true), null, PollInterval, PollInterval);
            volumes.VolumesChanged += OnVolumesChanged;
        }
    }

    /// <summary>
    /// Stops listening and polling, and cancels a running check.
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
            volumes.VolumesChanged -= OnVolumesChanged;
            debounce?.Dispose();
            poll?.Dispose();
        }

        stopping.Cancel();
    }

    private void OnVolumesChanged(object? source, EventArgs e)
    {
        lock (syncRoot)
        {
            // Each event pushes the check back, so a burst gives one check.
            if (!disposed)
            {
                debounce?.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private async Task CheckAsync(bool offlineOnly)
    {
        var token = stopping.Token;
        if (token.IsCancellationRequested)
        {
            return;
        }

        var cancel = offlineOnly ? await StartPollAsync(token).ConfigureAwait(false) : await StartVolumeCheckAsync(token).ConfigureAwait(false);
        if (cancel is null)
        {
            return;
        }

        try
        {
            var changed = await sender.Send(new CheckFolderAvailabilityCommand(OfflineOnly: offlineOnly), cancel.Token).ConfigureAwait(false);
            if (changed.Count > 0)
            {
                logger.LogInformation("Live check found {Count} folder(s) changed status", changed.Count);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // Shutting down, or a volume check took over from this poll.
        }
        catch (Exception exception)
        {
            // E.g. the library closed during the check. The next event or poll tries again.
            logger.LogWarning(exception, "Live folder availability check failed");
        }
        finally
        {
            lock (syncRoot)
            {
                if (ReferenceEquals(runningPoll, cancel))
                {
                    runningPoll = null;
                }
            }

            cancel.Dispose();
            gate.Release();
        }
    }

    /// <summary>
    /// Takes the gate for a poll, unless a check is running or a volume check is waiting.
    /// </summary>
    /// <returns>The poll's cancellation, which a volume check cancels, or <see langword="null"/> to skip it.</returns>
    private async Task<CancellationTokenSource?> StartPollAsync(CancellationToken token)
    {
        try
        {
            if (!await gate.WaitAsync(TimeSpan.Zero, token).ConfigureAwait(false))
            {
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        lock (syncRoot)
        {
            if (waitingVolumeChecks == 0)
            {
                runningPoll = CancellationTokenSource.CreateLinkedTokenSource(token);
                return runningPoll;
            }
        }

        gate.Release();
        return null;
    }

    /// <summary>
    /// Takes the gate for a volume check. A running check is waited for, since the volumes changed after it began, but a
    /// running poll is cancelled first: it may be stuck on an unreachable network share, and this check covers it.
    /// </summary>
    /// <returns>The check's cancellation, or <see langword="null"/> when the monitor stopped.</returns>
    private async Task<CancellationTokenSource?> StartVolumeCheckAsync(CancellationToken token)
    {
        lock (syncRoot)
        {
            waitingVolumeChecks++;

            // Disposed only after it stops being the running poll, under this lock.
            runningPoll?.Cancel();
        }

        try
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            lock (syncRoot)
            {
                waitingVolumeChecks--;
            }
        }

        return CancellationTokenSource.CreateLinkedTokenSource(token);
    }
}
