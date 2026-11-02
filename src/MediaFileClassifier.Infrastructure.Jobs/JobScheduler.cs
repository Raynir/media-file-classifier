using System.Threading.Channels;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Jobs;

/// <summary>
/// Runs the jobs of one open library (E05-S01-T02). One dispatcher loop, on the thread pool, keeps a small buffer of
/// queued work items per resource pool, read from the database in run order, and starts the best one whenever a pool
/// is under its limit. The database stays the source of truth: the buffer is only a read-ahead.
/// </summary>
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
    private readonly TimeProvider clock;
    private readonly ILogger logger;
    private readonly Lock gate = new();
    private readonly Dictionary<ResourcePool, PoolState> pools;
    private readonly Dictionary<Id<WorkItem>, RunningWork> running = [];
    private readonly HashSet<Id<ProcessingJob>> cancelledJobs = [];
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationTokenSource aborting = new();
    private HashSet<Id<MediaItem>> boosted = [];
    private bool boostChanged;
    private Task loop = Task.CompletedTask;
    private volatile bool paused;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobScheduler"/> class. Call <see cref="Start"/> to start it.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="handlers">The registered stage handlers.</param>
    /// <param name="limits">Concurrency limit of each pool.</param>
    /// <param name="clock">Clock.</param>
    /// <param name="logger">Logger.</param>
    public JobScheduler(
        IOpenLibrary library,
        IEnumerable<IStageHandler> handlers,
        IReadOnlyDictionary<ResourcePool, int> limits,
        TimeProvider clock,
        ILogger logger)
    {
        Library = library;
        this.handlers = handlers.ToDictionary(handler => handler.Stage, StringComparer.Ordinal);
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
        await Library.Jobs.AddJobAsync(job, CreateWorkItems(job.Id, job.Priority, request.WorkItems, now), cancellationToken).ConfigureAwait(false);
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
        var added = await Library.Jobs.AddWorkItemsAsync(jobId, CreateWorkItems(jobId, job.Priority, workItems, now), now, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Stops the scheduler before the library closes (E05-S01-T06): no new work starts, running work gets
    /// <see cref="StopGracePeriod"/> to finish, and what's still running then is cancelled and put back in the queue.
    /// </summary>
    /// <returns>A task that completes when no work is running and nothing more will be written.</returns>
    public async Task StopAsync()
    {
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
                        await LoadBoostedAsync(token).ConfigureAwait(false);
                        await DispatchAsync(token).ConfigureAwait(false);
                    }

                    await wake.Reader.ReadAsync(token).ConfigureAwait(false);
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    // E.g. a passing database error: read everything again after a pause.
                    LogDispatcherFailed(exception);
                    lock (gate)
                    {
                        boostChanged = true;
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
        lock (gate)
        {
            foreach (var workItem in queued)
            {
                var pool = pools[workItem.Pool];
                if (!running.ContainsKey(workItem.Id) && !pool.Buffer.Any(buffered => buffered.Id == workItem.Id))
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
                }

                var page = await Library.Jobs.GetQueuedAsync(resource, PageSize, excluding, token).ConfigureAwait(false);
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

            var finished = await HandleAsync(workItem, cancellation.Token).ConfigureAwait(false);
            await Library.Jobs.FinishAsync(finished, clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
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
    /// Runs the stage handler, and returns the work item in its finished state.
    /// </summary>
    private async Task<WorkItem> HandleAsync(WorkItem workItem, CancellationToken token)
    {
        if (!handlers.TryGetValue(workItem.Stage, out var handler))
        {
            return workItem.ChangeState(WorkItemState.Failed, clock.GetUtcNow(), $"No stage handler is registered for '{workItem.Stage}'.");
        }

        try
        {
            await handler.HandleAsync(new WorkItemContext(workItem, this), token).ConfigureAwait(false);
            return workItem.ChangeState(WorkItemState.Completed, clock.GetUtcNow());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelled with its job, or stopped because the library is closing: then it runs again next time.
            bool jobCancelled;
            lock (gate)
            {
                jobCancelled = cancelledJobs.Contains(workItem.JobId);
            }

            return workItem.ChangeState(jobCancelled ? WorkItemState.Cancelled : WorkItemState.Queued, clock.GetUtcNow());
        }
        catch (Exception exception)
        {
            LogWorkItemFailed(exception, workItem.Id.ToString(), workItem.Stage);
            return workItem.ChangeState(WorkItemState.Failed, clock.GetUtcNow(), exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Work item {WorkItemId} ({Stage}) failed")]
    private partial void LogWorkItemFailed(Exception exception, string workItemId, string stage);

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
