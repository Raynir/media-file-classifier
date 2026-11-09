using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Jobs.Errors;

/// <summary>
/// The app's <see cref="IProviderCircuitBreaker"/> (E05-S04-T03). A circuit opens after
/// <see cref="FailureThreshold"/> consecutive provider failures, or at once for a configuration problem. While it's
/// open, one probe per provider checks its health every <see cref="DefaultProbeInterval"/>; a healthy provider (or one
/// that only misses its model, D037) closes it.
/// </summary>
public sealed partial class ProviderCircuitBreaker : IProviderCircuitBreaker, IDisposable
{
    /// <summary>
    /// Consecutive provider failures that open a circuit.
    /// </summary>
    public const int FailureThreshold = 3;

    /// <summary>
    /// How often the health of an unavailable provider is checked.
    /// </summary>
    public static readonly TimeSpan DefaultProbeInterval = TimeSpan.FromSeconds(15);

    private readonly IAiProviderRegistry? registry;
    private readonly TimeProvider clock;
    private readonly ILogger logger;
    private readonly TimeSpan probeInterval;
    private readonly Lock gate = new();
    private readonly Dictionary<string, int> failures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Circuit> open = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource disposing = new();
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderCircuitBreaker"/> class.
    /// </summary>
    /// <param name="clock">Clock, for the probe interval.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="registry">Checks provider health, or <see langword="null"/> to close every circuit at the first probe.</param>
    /// <param name="probeInterval">How often an open circuit's provider is checked, or <see langword="null"/> for <see cref="DefaultProbeInterval"/>.</param>
    public ProviderCircuitBreaker(TimeProvider clock, ILogger<ProviderCircuitBreaker>? logger = null, IAiProviderRegistry? registry = null, TimeSpan? probeInterval = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        this.clock = clock;
        this.logger = logger ?? NullLogger<ProviderCircuitBreaker>.Instance;
        this.registry = registry;
        this.probeInterval = probeInterval ?? DefaultProbeInterval;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<ProviderCircuitStatus> OpenCircuits
    {
        get
        {
            lock (gate)
            {
                return [.. open.Values.Select(circuit => circuit.Status).OrderBy(status => status.OpenedAt)];
            }
        }
    }

    /// <inheritdoc />
    public bool IsOpen(string providerId)
    {
        ArgumentNullException.ThrowIfNull(providerId);

        lock (gate)
        {
            return open.ContainsKey(providerId);
        }
    }

    /// <inheritdoc />
    public void RecordSuccess(string providerId)
    {
        ArgumentNullException.ThrowIfNull(providerId);

        lock (gate)
        {
            failures.Remove(providerId);
        }
    }

    /// <inheritdoc />
    public bool RecordFailure(ClassifiedError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (!error.IsProviderFailure || error.IsModelMissing || error.ProviderId is not { } providerId)
        {
            return false;
        }

        Circuit circuit;
        lock (gate)
        {
            if (disposed)
            {
                return false;
            }

            if (open.ContainsKey(providerId))
            {
                return true;
            }

            var count = failures.GetValueOrDefault(providerId) + 1;
            if (count < FailureThreshold && error.Category != ErrorCategory.Configuration)
            {
                failures[providerId] = count;
                return false;
            }

            failures.Remove(providerId);
            var status = new ProviderCircuitStatus(
                providerId,
                registry?.Find(providerId)?.DisplayName ?? providerId,
                error.Message,
                clock.GetUtcNow(),
                error.Category == ErrorCategory.Configuration);
            circuit = new Circuit(status);
            open[providerId] = circuit;

            // Started under the lock, so Dispose can't miss it; the probe's first step is a delay.
            _ = Task.Run(() => ProbeLoopAsync(circuit, disposing.Token));
        }

        LogOpened(providerId, error.Message);
        OnChanged();
        return true;
    }

    /// <inheritdoc />
    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        List<Circuit> circuits;
        lock (gate)
        {
            circuits = [.. open.Values];
        }

        foreach (var circuit in circuits)
        {
            await ProbeAsync(circuit, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops the probes. Circuits stay as they are.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        disposing.Cancel();
        disposing.Dispose();
    }

    private async Task ProbeLoopAsync(Circuit circuit, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(probeInterval, clock, token).ConfigureAwait(false);
                try
                {
                    if (await ProbeAsync(circuit, token).ConfigureAwait(false))
                    {
                        return;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    // A probe must never take the app down: the provider stays unavailable until the next one.
                    LogProbeFailed(exception, circuit.Status.ProviderId);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Disposed.
        }
    }

    /// <summary>
    /// Checks the provider of an open circuit once, and closes the circuit if it's available.
    /// </summary>
    /// <returns>Whether the circuit is closed (by this check or another).</returns>
    private async Task<bool> ProbeAsync(Circuit circuit, CancellationToken token)
    {
        var providerId = circuit.Status.ProviderId;
        ProviderHealth? health;
        if (registry is null)
        {
            health = ProviderHealth.Healthy;
        }
        else
        {
            try
            {
                health = (await registry.CheckHealthAsync(providerId, token).ConfigureAwait(false)).Health;
            }
            catch (KeyNotFoundException)
            {
                // The provider was removed: nothing waits for it any more.
                health = null;
            }
        }

        var available = health is null || health.IsHealthy || health.IsWaitingForModel;
        bool changed;
        lock (gate)
        {
            if (!open.TryGetValue(providerId, out var current) || current != circuit)
            {
                return true;
            }

            if (available)
            {
                open.Remove(providerId);
                failures.Remove(providerId);
                changed = true;
            }
            else
            {
                var message = health!.Message ?? circuit.Status.Message;
                changed = message != circuit.Status.Message;
                circuit.Status = circuit.Status with { Message = message };
            }
        }

        if (available)
        {
            LogClosed(providerId);
        }

        if (changed)
        {
            OnChanged();
        }

        return available;
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI provider {ProviderId} is unavailable, work that needs it waits: {Reason}")]
    private partial void LogOpened(string providerId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI provider {ProviderId} is available again")]
    private partial void LogClosed(string providerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The health probe of AI provider {ProviderId} failed")]
    private partial void LogProbeFailed(Exception exception, string providerId);

    /// <summary>
    /// An open circuit. Its status is changed under the lock.
    /// </summary>
    /// <param name="status">The status when it opened.</param>
    private sealed class Circuit(ProviderCircuitStatus status)
    {
        public ProviderCircuitStatus Status { get; set; } = status;
    }
}
