using System.Threading.Channels;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Jobs.Errors;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Jobs;

/// <summary>
/// Runs the jobs of one open library (E05-S01-T02). One dispatcher loop, on the thread pool, keeps a small buffer of
/// queued work items per resource pool, read from the database in run order, and starts the best one whenever a pool
/// is under its limit. The database stays the source of truth: the buffer is only a read-ahead.
/// </summary>
/// <remarks>
/// Work of a stage whose model isn't installed isn't dispatched, and jobs left with only such work wait in
/// <see cref="ProcessingJobState.WaitingForModel"/> (D037). When a model is installed, they're queued again.
/// Work that failed because an AI provider is unavailable is parked until the provider's circuit closes (E05-S04-T03).
/// Work for an item whose folder is Offline is parked until the folder is Online again (D188, D025).
/// </remarks>
internal sealed partial class JobScheduler : IJobScheduler, IAsyncDisposable
{
    /// <summary>
    /// Number of queued work items read ahead per pool.
    /// </summary>
    private const int PageSize = 64;

    /// <summary>
    /// How long running work may take to finish when the library closes, before it's cancelled.
    /// </summary>
    public static readonly TimeSpan StopGracePeriod = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the dispatcher waits after an error before it reads the queue again.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyDictionary<string, IStageHandler> handlers;
    private readonly IReadOnlyDictionary<string, ModelPurpose> modelStages;
    private readonly IModelAvailability? models;
    private readonly JobProgressTracker progress;
    private readonly TimeProvider clock;
    private readonly ILogger logger;
    private readonly IErrorClassifier classifier;
    private readonly RetryPolicy retryPolicy;
    private readonly JobRetention retention;
    private readonly IProviderCircuitBreaker? breaker;
    private readonly Lock gate = new();
    private readonly Dictionary<ResourcePool, PoolState> pools;
    private readonly Dictionary<Id<WorkItem>, RunningWork> running = [];
    private readonly HashSet<Id<ProcessingJob>> cancelledJobs = [];
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationTokenSource aborting = new();

    // The providers each stage was seen failing with: a completed work item of the stage means they answered.
    private readonly Dictionary<string, HashSet<string>> stageProviders = new(StringComparer.Ordinal);
    private HashSet<Id<MediaItem>> boosted = [];
    private bool boostChanged;
    private HashSet<string> waitingStages = new(StringComparer.Ordinal);
    private bool modelsChanged = true;
    private int releaseRequests;
    private DateTimeOffset? nextRetryAt;
    private bool retriesChanged = true;

    // Parked work is released when a circuit closes, and when the library opens, because circuits live in memory.
    private bool releaseParked;
    private HashSet<string> openCircuits = new(StringComparer.Ordinal);

    // Retries due by this time were visible to the queue reads since the last OnRetriesDue; later ones need a wake-up.
    private DateTimeOffset retryHorizon = DateTimeOffset.MinValue;
    private Task loop = Task.CompletedTask;
    private volatile bool paused;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobScheduler"/> class. Call <see cref="Start"/> to start it.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="handlers">The registered stage handlers.</param>
    /// <param name="limits">Concurrency limit of each pool.</param>
    /// <param name="progress">The progress counters, kept up to date as work is added and finishes.</param>
    /// <param name="clock">Clock.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="models">
    /// Which models are installed, or <see langword="null"/> to dispatch every stage regardless.
    /// </param>
    /// <param name="classifier">Classifies failures, or <see langword="null"/> for the .NET exception rules only.</param>
    /// <param name="retryPolicy">When transient failures are retried, or <see langword="null"/> for <see cref="RetryPolicy.Default"/>.</param>
    /// <param name="breaker">Pauses unavailable AI providers, or <see langword="null"/> to retry their failures like any other.</param>
    /// <param name="retention">How long finished jobs are kept, or <see langword="null"/> for <see cref="JobRetention.Default"/>.</param>
    public JobScheduler(
        IOpenLibrary library,
        IEnumerable<IStageHandler> handlers,
        IReadOnlyDictionary<ResourcePool, int> limits,
        JobProgressTracker progress,
        TimeProvider clock,
        ILogger logger,
        IModelAvailability? models = null,
        IErrorClassifier? classifier = null,
        RetryPolicy? retryPolicy = null,
        IProviderCircuitBreaker? breaker = null,
        JobRetention? retention = null)
    {
        Library = library;
        this.retention = retention ?? JobRetention.Default;
        this.breaker = breaker;

        // Work parked in an earlier session runs again: its provider or folder may be back.
        releaseParked = true;
        if (breaker is not null)
        {
            openCircuits = [.. breaker.OpenCircuits.Select(circuit => circuit.ProviderId)];
            breaker.Changed += OnCircuitsChanged;
        }

        this.classifier = classifier ?? new ErrorClassifier([]);
        this.retryPolicy = retryPolicy ?? RetryPolicy.Default;
        this.handlers = handlers.ToDictionary(handler => handler.Stage, StringComparer.Ordinal);
        this.models = models;
        modelStages = models is null
            ? new Dictionary<string, ModelPurpose>()
            : this.handlers.Values.OfType<IModelDependentStage>().ToDictionary(stage => stage.Stage, stage => stage.RequiredModel, StringComparer.Ordinal);
        if (models is not null)
        {
            models.Changed += OnModelsChanged;
        }

        this.progress = progress;
        this.clock = clock;
        this.logger = logger;
        pools = Enum.GetValues<ResourcePool>().ToDictionary(pool => pool, pool => new PoolState(Math.Max(1, limits.GetValueOrDefault(pool, 1))));
    }

    /// <inheritdoc />
    public IOpenLibrary Library { get; }

    /// <inheritdoc />
    public bool IsPaused => paused;

    /// <summary>
    /// Starts the dispatcher loop.
    /// </summary>
    /// <param name="startPaused">Whether to wait for <see cref="Resume"/> before starting any work.</param>
    public void Start(bool startPaused)
    {
        paused = startPaused;
        loop = Task.Run(RunLoopAsync);
    }

    /// <inheritdoc />
    public async Task<Id<ProcessingJob>> EnqueueJobAsync(JobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);

        var now = clock.GetUtcNow();
        var job = new ProcessingJob
        {
            Id = Id<ProcessingJob>.New(),
            Type = request.Type,
            Description = request.Description,
            FolderId = request.FolderId,
            ItemIds = request.ItemIds,
            Priority = (int)request.Priority,
            CreatedAt = now,
        };
        var workItems = CreateWorkItems(job.Id, job.Priority, request.WorkItems, now);

        // Counted first, so a work item can't finish before its job is tracked.
        progress.AddWork(job, workItems);
        try
        {
            await Library.Jobs.AddJobAsync(job, workItems, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            progress.RemoveWork(job.Id, workItems);
            throw;
        }

        OnNewWork();
        return job.Id;
    }

    /// <inheritdoc />
    public async Task<bool> EnqueueWorkAsync(Id<ProcessingJob> jobId, IReadOnlyList<WorkItemRequest> workItems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItems);

        var job = await Library.Jobs.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return false;
        }

        var now = clock.GetUtcNow();
        var created = CreateWorkItems(jobId, job.Priority, workItems, now);
        progress.AddWork(job, created);
        var added = false;
        try
        {
            added = await Library.Jobs.AddWorkItemsAsync(jobId, created, now, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!added)
            {
                progress.RemoveWork(jobId, created);
            }
        }

        if (added)
        {
            OnNewWork();
        }

        return added;
    }

    /// <inheritdoc />
    public async Task<bool> CancelJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default)
    {
        // Mark the job first, so its running work items end as Cancelled, not queued again.
        List<CancellationTokenSource> toCancel;
        lock (gate)
        {
            cancelledJobs.Add(jobId);
        }

        var cancelled = false;
        try
        {
            cancelled = await Library.Jobs.CancelJobAsync(jobId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!cancelled)
            {
                lock (gate)
                {
                    cancelledJobs.Remove(jobId);
                }
            }
        }

        if (!cancelled)
        {
            return false;
        }

        progress.RemoveJob(jobId);

        lock (gate)
        {
            foreach (var pool in pools.Values)
            {
                pool.Buffer.RemoveAll(item => item.JobId == jobId);
            }

            toCancel = [.. running.Values.Where(work => work.JobId == jobId).Select(work => work.Cancellation)];
        }

        foreach (var cancellation in toCancel)
        {
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // The work item finished in the meantime.
            }
        }

        return true;
    }

    /// <inheritdoc />
    public void Boost(IReadOnlyCollection<Id<MediaItem>> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        lock (gate)
        {
            boosted = [.. itemIds];
            boostChanged = true;
        }

        Wake();
    }

    /// <inheritdoc />
    public int GetPoolLimit(ResourcePool pool)
    {
        lock (gate)
        {
            return pools[pool].Limit;
        }
    }

    /// <inheritdoc />
    public void SetPoolLimit(ResourcePool pool, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        lock (gate)
        {
            pools[pool].Limit = limit;
        }

        Wake();
    }

    /// <inheritdoc />
    public void Resume()
    {
        paused = false;
        Wake();
    }

    /// <inheritdoc />
    public void ReleaseParked()
    {
        lock (gate)
        {
            releaseParked = true;
            releaseRequests++;
        }

        Wake();
    }

    /// <summary>
    /// Stops the scheduler before the library closes (E05-S01-T06): no new work starts, running work gets
    /// <see cref="StopGracePeriod"/> to finish, and what's still running then is cancelled and put back in the queue.
    /// </summary>
    /// <returns>A task that completes when no work is running and nothing more will be written.</returns>
    public async Task StopAsync()
    {
        if (models is not null)
        {
            models.Changed -= OnModelsChanged;
        }

        if (breaker is not null)
        {
            breaker.Changed -= OnCircuitsChanged;
        }

        await stopping.CancelAsync().ConfigureAwait(false);
        await loop.ConfigureAwait(false);
        var all = Task.WhenAll(RunningTasks());
        if (await Task.WhenAny(all, Task.Delay(StopGracePeriod, clock)).ConfigureAwait(false) != all)
        {
            await aborting.CancelAsync().ConfigureAwait(false);
        }

        await all.ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the scheduler, see <see cref="StopAsync"/>.
    /// </summary>
    /// <returns>A task that completes when no work is running.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        stopping.Dispose();
        aborting.Dispose();
    }

    /// <summary>
    /// Gets the number of work items running in a pool, for tests and diagnostics.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <returns>The number of running work items.</returns>
    internal int GetRunningCount(ResourcePool pool)
    {
        lock (gate)
        {
            return pools[pool].Running;
        }
    }

    /// <summary>
    /// Gets whether a work item runs before another: user-initiated actions first, then boosted items, then by
    /// priority, then oldest first. Called under the lock.
    /// </summary>
    private bool RunsBefore(WorkItem candidate, WorkItem best)
    {
        var candidateRank = Rank(candidate);
        var bestRank = Rank(best);
        return candidateRank != bestRank
            ? candidateRank > bestRank
            : candidate.Priority != best.Priority
                ? candidate.Priority > best.Priority
                : candidate.CreatedAt != best.CreatedAt
                    ? candidate.CreatedAt < best.CreatedAt
                    : string.CompareOrdinal(candidate.Id.ToString(), best.Id.ToString()) < 0;
    }

    private int Rank(WorkItem workItem) =>
        workItem.Priority >= (int)JobPriority.UserAction ? 2
            : workItem.ItemId is { } itemId && boosted.Contains(itemId) ? 1
            : 0;

    private List<WorkItem> CreateWorkItems(Id<ProcessingJob> jobId, int priority, IReadOnlyList<WorkItemRequest> requests, DateTimeOffset now) =>
        requests.Select(request =>
        {
            if (!handlers.TryGetValue(request.Stage, out var handler))
            {
                throw new ArgumentException($"No stage handler is registered for '{request.Stage}'.", nameof(requests));
            }

            return WorkItem.Create(jobId, request.ItemId, request.Stage, handler.Pool, priority, request.Payload, now);
        }).ToList();

    private void OnNewWork()
    {
        lock (gate)
        {
            foreach (var pool in pools.Values)
            {
                pool.Exhausted = false;
                pool.Stale = true;
            }
        }

        Wake();
    }

    private void Wake() => wake.Writer.TryWrite(true);

    private Task[] RunningTasks()
    {
        lock (gate)
        {
            return [.. running.Values.Select(work => work.Task)];
        }
    }

    private async Task RunLoopAsync()
    {
        var token = stopping.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!paused)
                    {
                        await RefreshWaitingStagesAsync(token).ConfigureAwait(false);
                        await ReleaseParkedAsync(token).ConfigureAwait(false);
                        await LoadBoostedAsync(token).ConfigureAwait(false);
                        await DispatchAsync(token).ConfigureAwait(false);
                        await MarkWaitingForModelAsync(token).ConfigureAwait(false);
                    }

                    await WaitAsync(token).ConfigureAwait(false);
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    // E.g. a passing database error: read everything again after a pause.
                    LogDispatcherFailed(exception);
                    lock (gate)
                    {
                        boostChanged = true;
                        modelsChanged = true;
                        retriesChanged = true;
                        foreach (var pool in pools.Values)
                        {
                            pool.Exhausted = false;
                            pool.Stale = true;
                        }
                    }

                    await Task.Delay(RetryDelay, clock, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopping.
        }
    }

    /// <summary>
    /// After a model was installed or deleted (and at the start), finds the stages whose model is missing, and queues
    /// the jobs that waited for a model again. Those whose model is still missing wait again after the dispatch.
    /// </summary>
    private async Task RefreshWaitingStagesAsync(CancellationToken token)
    {
        lock (gate)
        {
            if (!modelsChanged)
            {
                return;
            }

            modelsChanged = false;
        }

        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (stage, purpose) in modelStages)
        {
            if (!await models!.IsInstalledAsync(purpose, token).ConfigureAwait(false))
            {
                missing.Add(stage);
            }
        }

        lock (gate)
        {
            waitingStages = missing;
            foreach (var pool in pools.Values)
            {
                pool.Buffer.RemoveAll(workItem => missing.Contains(workItem.Stage));
                pool.Exhausted = false;
                pool.Stale = true;
            }
        }

        var resumed = await Library.Jobs.ResumeWaitingForModelAsync(clock.GetUtcNow(), token).ConfigureAwait(false);
        if (resumed > 0)
        {
            LogJobsResumed(resumed);
        }
    }

    /// <summary>
    /// Puts the jobs that have only work waiting for a model left into <see cref="ProcessingJobState.WaitingForModel"/>.
    /// </summary>
    private async Task MarkWaitingForModelAsync(CancellationToken token)
    {
        List<string> stages;
        lock (gate)
        {
            if (waitingStages.Count == 0)
            {
                return;
            }

            stages = [.. waitingStages];
        }

        var waiting = await Library.Jobs.MarkWaitingForModelAsync(stages, clock.GetUtcNow(), token).ConfigureAwait(false);
        if (waiting > 0)
        {
            LogJobsWaiting(waiting, string.Join(", ", stages));
        }
    }

    /// <summary>
    /// Releases the parked work after a circuit closed, a folder came back Online (and when the scheduler starts), so it
    /// runs again. Work for a provider or folder that's still unavailable is parked again when it fails.
    /// </summary>
    private async Task ReleaseParkedAsync(CancellationToken token)
    {
        lock (gate)
        {
            if (!releaseParked)
            {
                return;
            }

            releaseParked = false;
        }

        int released;
        try
        {
            released = await Library.Jobs.ReleaseParkedAsync(clock.GetUtcNow(), token).ConfigureAwait(false);
        }
        catch
        {
            // Only a release that didn't happen is tried again: releasing parked work for nothing re-runs all of it.
            lock (gate)
            {
                releaseParked = true;
            }

            throw;
        }

        if (released > 0)
        {
            LogParkedReleased(released);
            lock (gate)
            {
                foreach (var pool in pools.Values)
                {
                    pool.Exhausted = false;
                    pool.Stale = true;
                }
            }
        }
    }

    private void OnCircuitsChanged(object? sender, EventArgs e)
    {
        lock (gate)
        {
            // Read under the lock, so events racing on two threads can't store an older set over a newer one.
            var nowOpen = breaker!.OpenCircuits.Select(circuit => circuit.ProviderId).ToHashSet(StringComparer.Ordinal);
            if (openCircuits.Any(providerId => !nowOpen.Contains(providerId)))
            {
                releaseParked = true;
            }

            openCircuits = nowOpen;
        }

        Wake();
    }

    private void OnModelsChanged(object? sender, EventArgs e)
    {
        lock (gate)
        {
            modelsChanged = true;
        }

        Wake();
    }

    /// <summary>
    /// After <see cref="Boost"/>, reads the boosted items' queued work into the buffers, because it may be far down
    /// the queue.
    /// </summary>
    private async Task LoadBoostedAsync(CancellationToken token)
    {
        List<Id<MediaItem>> itemIds;
        lock (gate)
        {
            if (!boostChanged)
            {
                return;
            }

            boostChanged = false;
            itemIds = [.. boosted];
        }

        if (itemIds.Count == 0)
        {
            return;
        }

        var queued = await Library.Jobs.GetQueuedForItemsAsync(itemIds, token).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        lock (gate)
        {
            // A retry that isn't due yet runs when it's due, boosted or not.
            foreach (var workItem in queued.Where(workItem => workItem.RetryAt is null || workItem.RetryAt <= now))
            {
                var pool = pools[workItem.Pool];
                if (!running.ContainsKey(workItem.Id) && !waitingStages.Contains(workItem.Stage) && !pool.Buffer.Any(buffered => buffered.Id == workItem.Id))
                {
                    pool.Buffer.Add(workItem);
                }
            }
        }
    }

    /// <summary>
    /// Starts work in every pool until it's at its limit or has nothing queued.
    /// </summary>
    private async Task DispatchAsync(CancellationToken token)
    {
        foreach (var (resource, pool) in pools)
        {
            while (!token.IsCancellationRequested && !paused)
            {
                List<Id<WorkItem>> excluding;
                List<string> excludingStages;
                lock (gate)
                {
                    if (pool.Running >= pool.Limit)
                    {
                        break;
                    }

                    if (!pool.Stale && (pool.Buffer.Count > 0 || pool.Exhausted))
                    {
                        var next = TakeNext(pool);
                        if (next is null)
                        {
                            break;
                        }

                        StartWorkItem(next, pool);
                        continue;
                    }

                    pool.Stale = false;
                    excluding = [.. running.Keys];
                    excludingStages = [.. waitingStages];
                }

                var page = await Library.Jobs.GetQueuedAsync(resource, PageSize, excluding, excludingStages, clock.GetUtcNow(), token).ConfigureAwait(false);
                lock (gate)
                {
                    // The page replaces the read-ahead, so it stays small; boosted work read by LoadBoostedAsync stays.
                    pool.Buffer.RemoveAll(item => Rank(item) != 1);
                    pool.Buffer.AddRange(page.Where(item => !running.ContainsKey(item.Id) && !pool.Buffer.Any(buffered => buffered.Id == item.Id)));
                    pool.Exhausted = page.Count == 0;
                }
            }
        }
    }

    /// <summary>
    /// Takes the work item that should run next out of a pool's buffer. Called under the lock.
    /// </summary>
    private WorkItem? TakeNext(PoolState pool)
    {
        if (pool.Buffer.Count == 0)
        {
            return null;
        }

        var bestIndex = 0;
        for (var i = 1; i < pool.Buffer.Count; i++)
        {
            if (RunsBefore(pool.Buffer[i], pool.Buffer[bestIndex]))
            {
                bestIndex = i;
            }
        }

        var best = pool.Buffer[bestIndex];
        pool.Buffer.RemoveAt(bestIndex);
        return best;
    }

    // Deleting old jobs is housekeeping: a failure to do it must not fail the work item that triggered it.
    private async Task PruneJobsAsync()
    {
        try
        {
            await JobRetentionPruner.PruneAsync(Library.Jobs, retention, clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPruneFailed(exception);
        }
    }

    /// <summary>
    /// Starts a work item on the thread pool. Called under the lock, so the work item is registered as running
    /// before it can finish.
    /// </summary>
    private void StartWorkItem(WorkItem workItem, PoolState pool)
    {
        pool.Running++;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(aborting.Token);
        var task = Task.Run(() => RunWorkItemAsync(workItem, pool, cancellation));
        running[workItem.Id] = new RunningWork(task, cancellation, workItem.JobId);
    }

    private async Task RunWorkItemAsync(WorkItem queued, PoolState pool, CancellationTokenSource cancellation)
    {
        try
        {
            var workItem = await Library.Jobs.StartAsync(queued, clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
            if (workItem is null)
            {
                return;
            }

            int releasesBefore;
            lock (gate)
            {
                releasesBefore = releaseRequests;
            }

            var (finished, parkedFor) = await HandleAsync(workItem, cancellation.Token).ConfigureAwait(false);
            var job = await Library.Jobs.FinishAsync(finished, clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
            progress.Finished(finished, job);
            if (ProcessingJobStateTransitions.IsFinished(job.State))
            {
                await PruneJobsAsync().ConfigureAwait(false);
            }

            if (parkedFor is not null)
            {
                // The circuit may have closed before the parked state was written, so its release missed this one.
                if (!breaker!.IsOpen(parkedFor))
                {
                    lock (gate)
                    {
                        releaseParked = true;
                    }
                }
            }
            else if (finished.IsParked)
            {
                // Parked for its folder: a release asked for while it ran happened before the parked state was written.
                lock (gate)
                {
                    if (releaseRequests != releasesBefore)
                    {
                        releaseParked = true;
                    }
                }
            }
            else if (finished.RetryAt is { } retryAt)
            {
                lock (gate)
                {
                    nextRetryAt = nextRetryAt is { } next && next < retryAt ? next : retryAt;
                }
            }
        }
        catch (Exception exception)
        {
            LogWorkItemNotRecorded(exception, queued.Id.ToString(), queued.Stage);
        }
        finally
        {
            lock (gate)
            {
                running.Remove(queued.Id);
                pool.Running--;
            }

            cancellation.Dispose();

            Wake();
        }
    }

    /// <summary>
    /// Runs the stage handler, and returns the work item in its finished state, and the provider it waits for if it's
    /// parked.
    /// </summary>
    private async Task<(WorkItem WorkItem, string? ParkedFor)> HandleAsync(WorkItem workItem, CancellationToken token)
    {
        if (!handlers.TryGetValue(workItem.Stage, out var handler))
        {
            return (workItem.ChangeState(WorkItemState.Failed, clock.GetUtcNow(), $"No stage handler is registered for '{workItem.Stage}'."), null);
        }

        try
        {
            await handler.HandleAsync(new WorkItemContext(workItem, this), token).ConfigureAwait(false);
            RecordProviderSuccess(workItem.Stage);
            return (workItem.ChangeState(WorkItemState.Completed, clock.GetUtcNow()), null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelled with its job, or stopped because the library is closing: then it runs again next time.
            bool jobCancelled;
            lock (gate)
            {
                jobCancelled = cancelledJobs.Contains(workItem.JobId);
            }

            return (workItem.ChangeState(jobCancelled ? WorkItemState.Cancelled : WorkItemState.Queued, clock.GetUtcNow()), null);
        }
        catch (Exception exception)
        {
            return await OnFailedAsync(workItem, handler, exception).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tells the breaker that the providers a stage was seen failing with answered, now that a work item of the stage
    /// completed.
    /// </summary>
    private void RecordProviderSuccess(string stage)
    {
        if (breaker is null)
        {
            return;
        }

        List<string> providerIds;
        lock (gate)
        {
            if (!stageProviders.TryGetValue(stage, out var known))
            {
                return;
            }

            providerIds = [.. known];
        }

        foreach (var providerId in providerIds)
        {
            breaker.RecordSuccess(providerId);
        }
    }

    /// <summary>
    /// Decides what happens to a work item that failed (E05-S04): a transient failure is retried later, a missing model
    /// waits, a failure of an unavailable AI provider is parked until the provider is back, and anything else fails the
    /// work item for good and tells the stage.
    /// </summary>
    private async Task<(WorkItem WorkItem, string? ParkedFor)> OnFailedAsync(WorkItem workItem, IStageHandler handler, Exception exception)
    {
        var error = classifier.Classify(exception);
        var now = clock.GetUtcNow();
        if (error.IsFolderOffline)
        {
            // D188, D025: nothing to retry until the folder is back, and the item keeps its status.
            LogWorkItemWaitsForFolder(workItem.Id.ToString(), workItem.Stage, error.Message);
            return (workItem.Park(error.Category, error.Message, now), null);
        }

        if (error.IsModelMissing)
        {
            // D037: the dispatcher checks the models again, and holds the stage back while the model is missing.
            lock (gate)
            {
                modelsChanged = true;
            }

            LogWorkItemWaitsForModel(workItem.Id.ToString(), workItem.Stage, error.Message);
            return (workItem.ScheduleRetry(now + retryPolicy.MaxDelay, error.Category, error.Message, countsAsRetry: false, now), null);
        }

        if (breaker is not null && error is { IsProviderFailure: true, ProviderId: { } providerId })
        {
            lock (gate)
            {
                if (!stageProviders.TryGetValue(workItem.Stage, out var known))
                {
                    stageProviders[workItem.Stage] = known = new HashSet<string>(StringComparer.Ordinal);
                }

                known.Add(providerId);
            }

            if (breaker.RecordFailure(error))
            {
                LogWorkItemParked(workItem.Id.ToString(), workItem.Stage, providerId);
                return (workItem.Park(error.Category, error.Message, now), providerId);
            }
        }

        if (error.Category == ErrorCategory.Transient && retryPolicy.CanRetry(workItem.RetryCount))
        {
            var delay = retryPolicy.GetDelay(workItem.RetryCount);
            LogWorkItemRetrying(exception, workItem.Id.ToString(), workItem.Stage, workItem.RetryCount + 1, delay.TotalSeconds);
            return (workItem.ScheduleRetry(now + delay, error.Category, error.Message, countsAsRetry: true, now), null);
        }

        LogWorkItemFailed(exception, workItem.Id.ToString(), workItem.Stage, error.Category);
        if (handler is IFailureAwareStage stage)
        {
            try
            {
                await stage.OnFailedAsync(new WorkItemContext(workItem, this), error, aborting.Token).ConfigureAwait(false);
            }
            catch (Exception recordException)
            {
                LogFailureNotRecorded(recordException, workItem.Id.ToString(), workItem.Stage);
            }
        }

        return (workItem.Fail(clock.GetUtcNow(), error.Category, error.Message), null);
    }

    /// <summary>
    /// Waits for new work, or until the next retry is due (E05-S04-T02).
    /// </summary>
    private async Task WaitAsync(CancellationToken token)
    {
        bool refresh;
        DateTimeOffset horizon;
        lock (gate)
        {
            refresh = retriesChanged && !paused;
            horizon = retryHorizon;
            if (refresh)
            {
                retriesChanged = false;
            }
        }

        if (refresh)
        {
            var queued = await Library.Jobs.GetNextRetryAtAsync(horizon, token).ConfigureAwait(false);
            lock (gate)
            {
                // A retry recorded while reading is already in nextRetryAt.
                nextRetryAt = queued is { } read && (nextRetryAt is null || read < nextRetryAt) ? read : nextRetryAt;
            }
        }

        DateTimeOffset? due;
        lock (gate)
        {
            due = paused ? null : nextRetryAt;
        }

        var delay = due is { } at ? at - clock.GetUtcNow() : Timeout.InfiniteTimeSpan;
        if (due is not null && delay <= TimeSpan.Zero)
        {
            OnRetriesDue();
            return;
        }

        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        var woken = wake.Reader.WaitToReadAsync(waiting.Token).AsTask();
        var timer = Task.Delay(delay, clock, waiting.Token);
        var first = await Task.WhenAny(woken, timer).ConfigureAwait(false);
        await waiting.CancelAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (first == woken)
        {
            wake.Reader.TryRead(out _);
        }
        else
        {
            OnRetriesDue();
        }
    }

    /// <summary>
    /// Makes the dispatcher read the queue again, now that a retry is due, and look up the one after it.
    /// </summary>
    private void OnRetriesDue()
    {
        lock (gate)
        {
            // Every pool reads the queue again with a later due time, so it sees every retry due by now.
            retryHorizon = clock.GetUtcNow();
            nextRetryAt = null;
            retriesChanged = true;
            foreach (var pool in pools.Values)
            {
                pool.Exhausted = false;
                pool.Stale = true;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} jobs wait for a model (stages: {Stages})")]
    private partial void LogJobsWaiting(int count, string stages);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} jobs that waited for a model are queued again")]
    private partial void LogJobsResumed(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Work item {WorkItemId} ({Stage}) failed ({Category})")]
    private partial void LogWorkItemFailed(Exception exception, string workItemId, string stage, ErrorCategory category);

    [LoggerMessage(Level = LogLevel.Information, Message = "Work item {WorkItemId} ({Stage}) failed; retry {Retry} in {DelaySeconds:0.0} s")]
    private partial void LogWorkItemRetrying(Exception exception, string workItemId, string stage, int retry, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Work item {WorkItemId} ({Stage}) waits for a model: {Reason}")]
    private partial void LogWorkItemWaitsForModel(string workItemId, string stage, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Work item {WorkItemId} ({Stage}) waits until AI provider {ProviderId} is available")]
    private partial void LogWorkItemParked(string workItemId, string stage, string providerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Work item {WorkItemId} ({Stage}) waits until its folder is online: {Reason}")]
    private partial void LogWorkItemWaitsForFolder(string workItemId, string stage, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} parked work items are queued again")]
    private partial void LogParkedReleased(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The stage {Stage} couldn't record the failure of work item {WorkItemId}")]
    private partial void LogFailureNotRecorded(Exception exception, string workItemId, string stage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Old finished jobs couldn't be deleted")]
    private partial void LogPruneFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The state of work item {WorkItemId} ({Stage}) couldn't be recorded")]
    private partial void LogWorkItemNotRecorded(Exception exception, string workItemId, string stage);

    [LoggerMessage(Level = LogLevel.Error, Message = "The job dispatcher failed, and retries in a second")]
    private partial void LogDispatcherFailed(Exception exception);

    /// <summary>
    /// A running work item: its task, and how to cancel it.
    /// </summary>
    /// <param name="Task">Runs the work item and records its result.</param>
    /// <param name="Cancellation">Cancels the handler.</param>
    /// <param name="JobId">The work item's job.</param>
    private sealed record RunningWork(Task Task, CancellationTokenSource Cancellation, Id<ProcessingJob> JobId);

    /// <summary>
    /// A resource pool: its limit, its running count, and its read-ahead buffer.
    /// </summary>
    /// <param name="limit">Concurrency limit.</param>
    private sealed class PoolState(int limit)
    {
        public int Limit { get; set; } = limit;

        public int Running { get; set; }

        public List<WorkItem> Buffer { get; } = [];

        /// <summary>
        /// Gets or sets whether the last read found nothing queued, so the pool waits for new work before reading again.
        /// </summary>
        public bool Exhausted { get; set; }

        /// <summary>
        /// Gets or sets whether work was added since the last read, so the buffer may miss work that should run first.
        /// </summary>
        public bool Stale { get; set; } = true;
    }
}
