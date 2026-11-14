using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Jobs.Errors;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Jobs;

/// <summary>
/// Runs one <see cref="JobScheduler"/> for the open library, and recovers interrupted work when a library opens
/// (E05-S01-T05).
/// </summary>
/// <param name="handlers">The registered stage handlers.</param>
/// <param name="settings">App settings, for the pool limits and the resume mode.</param>
/// <param name="hardware">Hardware facts, for the default CPU pool limit.</param>
/// <param name="progress">The progress counters of the open library (E05-S02).</param>
/// <param name="clock">Clock.</param>
/// <param name="logger">Logger.</param>
/// <param name="models">Which models are installed, so stages that need a missing one wait (D037).</param>
/// <param name="classifier">Classifies failures, so transient ones are retried (E05-S04).</param>
/// <param name="retryPolicy">When transient failures are retried, or <see langword="null"/> for the default.</param>
/// <param name="breaker">Pauses unavailable AI providers (E05-S04-T03).</param>
/// <param name="retention">How long finished jobs are kept, or <see langword="null"/> for the default (E05-S03-T04).</param>
public sealed partial class JobSystem(
    IEnumerable<IStageHandler> handlers,
    ISettingsService settings,
    IHardwareInfo hardware,
    JobProgressTracker progress,
    TimeProvider clock,
    ILogger<JobSystem> logger,
    IModelAvailability? models = null,
    IErrorClassifier? classifier = null,
    RetryPolicy? retryPolicy = null,
    IProviderCircuitBreaker? breaker = null,
    JobRetention? retention = null) : IJobSystem, IDisposable
{
    /// <summary>
    /// Default concurrency limit of the I/O pool.
    /// </summary>
    public const int DefaultIoConcurrency = 4;

    /// <summary>
    /// Default concurrency limit of the AI pool.
    /// </summary>
    public const int DefaultAiConcurrency = 1;

    private readonly SemaphoreSlim lifecycle = new(1, 1);
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Stopped by StopAsync, before the library closes; Dispose can't wait for it.")]
    private volatile JobScheduler? current;

    /// <inheritdoc />
    public IJobScheduler? Current => current;

    /// <summary>
    /// Gets the concurrency limit of each pool: the setting if there is one, otherwise the default.
    /// </summary>
    /// <param name="processing">Processing settings.</param>
    /// <param name="hardware">Hardware facts.</param>
    /// <returns>The limit of each pool.</returns>
    public static IReadOnlyDictionary<ResourcePool, int> GetPoolLimits(ProcessingSettings processing, IHardwareInfo hardware)
    {
        ArgumentNullException.ThrowIfNull(processing);
        ArgumentNullException.ThrowIfNull(hardware);

        return new Dictionary<ResourcePool, int>
        {
            [ResourcePool.Io] = Math.Max(1, processing.IoConcurrency ?? DefaultIoConcurrency),
            [ResourcePool.Cpu] = Math.Max(1, processing.CpuConcurrency ?? hardware.DefaultParallelism),
            [ResourcePool.Ai] = Math.Max(1, processing.AiConcurrency ?? DefaultAiConcurrency),
        };
    }

    /// <inheritdoc />
    public async Task<IJobScheduler> StartAsync(IOpenLibrary library, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);

        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCurrentAsync().ConfigureAwait(false);

            var requeued = await library.Jobs.RequeueRunningAsync(clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            try
            {
                await JobRetentionPruner.PruneAsync(library.Jobs, retention ?? JobRetention.Default, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Housekeeping: old jobs stay until the next time, and the library still opens.
                LogPruneFailed(exception);
            }

            var pending = await library.Jobs.CountPendingAsync(cancellationToken).ConfigureAwait(false);
            var processing = settings.Current.Processing;
            var startPaused = pending > 0 && processing.ResumeOnOpen == ResumeMode.Ask;
            progress.Reset(
                await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false),
                await library.Jobs.CountUnfinishedWorkAsync(cancellationToken).ConfigureAwait(false));
            var scheduler = new JobScheduler(library, handlers, GetPoolLimits(processing, hardware), progress, clock, logger, models, classifier, retryPolicy, breaker, retention);
            scheduler.Start(startPaused);
            current = scheduler;
            LogStarted(library.Layout.RootDirectory, pending, requeued, startPaused);
            return scheduler;
        }
        finally
        {
            lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCurrentAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
        current is { } scheduler ? scheduler.Library.Jobs.CountPendingAsync(cancellationToken) : Task.FromResult(0);

    /// <summary>
    /// Releases the lifecycle lock. Call <see cref="StopAsync"/> first: disposing doesn't stop running work.
    /// </summary>
    public void Dispose() => lifecycle.Dispose();

    private async Task StopCurrentAsync()
    {
        if (current is { } scheduler)
        {
            current = null;
            await scheduler.DisposeAsync().ConfigureAwait(false);
            progress.Clear();
            LogStopped(scheduler.Library.Layout.RootDirectory);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started processing for {Directory}: {Pending} work items pending, {Requeued} put back after an interruption, paused: {Paused}")]
    private partial void LogStarted(string directory, int pending, int requeued, bool paused);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped processing for {Directory}")]
    private partial void LogStopped(string directory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Old finished jobs couldn't be deleted")]
    private partial void LogPruneFailed(Exception exception);
}
