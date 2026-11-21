using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;

/// <summary>
/// Keeps the open library's folder statuses live (E14-S07-T04, DNEW-4, amends D188). When a volume arrives or goes
/// away, every folder is checked once the events have been quiet for <see cref="Debounce"/>; and every
/// <see cref="PollInterval"/> the Offline folders alone are checked, because a network share coming back raises no
/// volume event. Online folders aren't polled. Checks run one at a time; a poll that finds a check running is skipped.
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

        // A volume check waits for a running one, since the volumes changed after it began; a poll just skips.
        try
        {
            if (offlineOnly)
            {
                if (!await gate.WaitAsync(TimeSpan.Zero, token).ConfigureAwait(false))
                {
                    return;
                }
            }
            else
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var changed = await sender.Send(new CheckFolderAvailabilityCommand(OfflineOnly: offlineOnly), token).ConfigureAwait(false);
            if (changed.Count > 0)
            {
                logger.LogInformation("Live check found {Count} folder(s) changed status", changed.Count);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            // E.g. the library closed during the check. The next event or poll tries again.
            logger.LogWarning(exception, "Live folder availability check failed");
        }
        finally
        {
            gate.Release();
        }
    }
}
