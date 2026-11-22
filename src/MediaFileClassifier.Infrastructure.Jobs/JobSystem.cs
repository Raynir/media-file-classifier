using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
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
/// <param name="settingsStore">Gets the setting values, for the pool limits and the resume mode. It's a function because the store reads the open library, which depends on this job system.</param>
/// <param name="processing">The processing setting definitions.</param>
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
    Func<ISettingsStore> settingsStore,
    ProcessingSettings processing,
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
    public const int DefaultIoConcurrency = ProcessingSettings.DefaultIoConcurrency;

    /// <summary>
    /// Default concurrency limit of the AI pool.
    /// </summary>
    public const int DefaultAiConcurrency = ProcessingSettings.DefaultAiConcurrency;

    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly HashSet<Task> pendingSaves = [];
    private ISettingsStore? subscribedTo;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Stopped by StopAsync, before the library closes; Dispose can't wait for it.")]
    private volatile JobScheduler? current;

    /// <inheritdoc />
    public IJobScheduler? Current => current;

    /// <summary>
    /// Gets the concurrency limit of each pool from the settings.
    /// </summary>
    /// <returns>The limit of each pool.</returns>
    public IReadOnlyDictionary<ResourcePool, int> GetPoolLimits() => new Dictionary<ResourcePool, int>
    {
        [ResourcePool.Io] = settingsStore().Get(processing.IoConcurrency),
        [ResourcePool.Cpu] = settingsStore().Get(processing.CpuConcurrency),
        [ResourcePool.Ai] = settingsStore().Get(processing.AiConcurrency),
    };

    /// <inheritdoc />
    public async Task<IJobScheduler> StartAsync(IOpenLibrary library, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);

        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCurrentAsync().ConfigureAwait(false);

            if (subscribedTo is null)
            {
                subscribedTo = settingsStore();
                subscribedTo.Changed += OnSettingChanged;
            }

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

            // The pause the user left the library in (E05-S05-T03), or nothing paused when it isn't kept.
            var saved = library.Library.Settings;
            var keepPaused = settingsStore().Get(processing.KeepPausedAfterRestart);
            if (!keepPaused)
            {
                await ForgetPauseAsync(library, cancellationToken).ConfigureAwait(false);
            }

            var pending = await library.Jobs.CountPendingAsync(cancellationToken).ConfigureAwait(false);
            var restorePaused = keepPaused && saved.ProcessingPaused;
            var startPaused = restorePaused || (pending > 0 && settingsStore().Get(processing.ResumeOnOpen) == ResumeMode.Ask);
            var unfinished = await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false);
            var counts = await library.Jobs.CountUnfinishedWorkAsync(cancellationToken).ConfigureAwait(false);
            if (!startPaused)
            {
                progress.Reset(unfinished, counts);
            }

            var scheduler = new JobScheduler(library, handlers, GetPoolLimits(), progress, clock, logger, models, classifier, retryPolicy, breaker, retention);
            if (keepPaused)
            {
                foreach (var pool in saved.PausedPools)
                {
                    scheduler.PausePool(pool);
                }
            }

            scheduler.PauseChanged += OnPauseChanged;
            scheduler.Start(startPaused);
            current = scheduler;
            if (startPaused)
            {
                // Told after the scheduler is current, so the status bar sees that processing waits for Resume.
                progress.Reset(unfinished, counts);
            }

            // A limit changed after it was read above and before the scheduler was current is applied now.
            ApplyPoolLimits();
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
    public void Dispose()
    {
        if (subscribedTo is not null)
        {
            subscribedTo.Changed -= OnSettingChanged;
        }

        lifecycle.Dispose();
    }

    // A pool limit that changes takes effect at once: more work starts now, and a lower limit applies as work finishes.
    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Setting == processing.IoConcurrency || e.Setting == processing.CpuConcurrency || e.Setting == processing.AiConcurrency)
        {
            ApplyPoolLimits();
        }
    }

    private void ApplyPoolLimits()
    {
        if (current is not { } scheduler)
        {
            return;
        }

        foreach (var (pool, limit) in GetPoolLimits())
        {
            scheduler.SetPoolLimit(pool, limit);
        }
    }

    /// <summary>
    /// Opens a library with nothing paused: resumes its paused jobs, and clears the stored pause.
    /// </summary>
    private async Task ForgetPauseAsync(IOpenLibrary library, CancellationToken cancellationToken)
    {
        var resumed = await library.Jobs.ResumeAllJobsAsync(clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (resumed > 0)
        {
            LogPausedJobsResumed(resumed);
        }

        if (library.Library.Settings is { ProcessingPaused: false, PausedPools.Count: 0 })
        {
            return;
        }

        await library.UpdateSettingsAsync(settings => settings with { ProcessingPaused = false, PausedPools = [] }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores the pause in the library settings, so it can be restored when the library opens again (E05-S05-T03).
    /// </summary>
    private void OnPauseChanged(object? sender, EventArgs e)
    {
        if (sender is JobScheduler scheduler)
        {
            var save = SavePauseAsync(scheduler);
            lock (pendingSaves)
            {
                pendingSaves.Add(save);
            }

            _ = save.ContinueWith(
                done =>
                {
                    lock (pendingSaves)
                    {
                        pendingSaves.Remove(done);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task SavePauseAsync(JobScheduler scheduler)
    {
        try
        {
            // Read inside the settings step, so the last of several quick changes is what's stored.
            await scheduler.Library.UpdateSettingsAsync(
                settings => settings with
                {
                    ProcessingPaused = scheduler.IsPaused,
                    PausedPools = [.. Enum.GetValues<ResourcePool>().Where(scheduler.IsPoolPaused)],
                }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // E.g. the library closed in the meantime: the pause still applies now, it just isn't kept.
            LogPauseNotSaved(exception);
        }
    }

    private async Task StopCurrentAsync()
    {
        if (current is { } scheduler)
        {
            scheduler.PauseChanged -= OnPauseChanged;
            current = null;
            await scheduler.DisposeAsync().ConfigureAwait(false);

            // A pause made just before the library closes is still stored. SavePauseAsync doesn't throw.
            Task[] saves;
            lock (pendingSaves)
            {
                saves = [.. pendingSaves];
            }

            await Task.WhenAll(saves).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} paused jobs were resumed, because paused processing isn't kept after a restart")]
    private partial void LogPausedJobsResumed(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The paused state of processing couldn't be saved")]
    private partial void LogPauseNotSaved(Exception exception);
}
