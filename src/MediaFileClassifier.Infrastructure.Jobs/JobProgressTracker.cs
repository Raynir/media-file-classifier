using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.Infrastructure.Jobs;

/// <summary>
/// Keeps the progress counters of the open library's unfinished jobs in memory (E05-S02-T01). The job system seeds it
/// from the database when a library opens, and the scheduler updates it as work items are added and finish, so
/// reading <see cref="Current"/> never touches the database.
/// </summary>
/// <remarks>
/// The estimated time remaining divides the pending work items by the throughput of the last
/// <see cref="ThroughputWindow"/> (a shorter window right after the start), and is shown once
/// <see cref="MinimumFinishedForEstimate"/> work items finished and <see cref="MinimumElapsedForEstimate"/> passed.
/// It's hidden while a job's total still grows.
/// </remarks>
/// <param name="clock">Clock.</param>
public sealed class JobProgressTracker(TimeProvider clock) : IJobProgress
{
    /// <summary>
    /// How many finished work items the estimate needs.
    /// </summary>
    public const int MinimumFinishedForEstimate = 10;

    /// <summary>
    /// How far back the throughput is measured.
    /// </summary>
    public static readonly TimeSpan ThroughputWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a job must have been tracked before it gets an estimate.
    /// </summary>
    public static readonly TimeSpan MinimumElapsedForEstimate = TimeSpan.FromSeconds(5);

    private readonly Lock gate = new();
    private readonly List<JobCounters> jobs = [];
    private Throughput overall = new(clock.GetUtcNow());

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public ProgressSummary Current
    {
        get
        {
            var now = clock.GetUtcNow();
            lock (gate)
            {
                if (jobs.Count == 0)
                {
                    return ProgressSummary.Empty;
                }

                var snapshots = jobs.Select(job => job.ToProgress(now)).ToList();
                var remaining = snapshots.Any(job => job.IsTotalGrowing)
                    ? null
                    : overall.Estimate(snapshots.Sum(job => job.Pending), now);
                return new ProgressSummary(snapshots, remaining);
            }
        }
    }

    /// <summary>
    /// Replaces the counters with those of a library that was just opened.
    /// </summary>
    /// <param name="unfinished">The library's unfinished jobs, oldest first.</param>
    /// <param name="counts">Their work items, counted by job, stage and state.</param>
    public void Reset(IEnumerable<ProcessingJob> unfinished, IEnumerable<WorkItemCount> counts)
    {
        ArgumentNullException.ThrowIfNull(unfinished);
        ArgumentNullException.ThrowIfNull(counts);

        var now = clock.GetUtcNow();
        lock (gate)
        {
            jobs.Clear();
            overall = new Throughput(now);
            jobs.AddRange(unfinished.Select(job => new JobCounters(job, now)));

            // Whole-target stages first: they usually feed the others.
            foreach (var count in counts.OrderBy(count => !count.IsWholeTarget).ThenBy(count => count.Stage, StringComparer.Ordinal))
            {
                if (Find(count.JobId) is { } job)
                {
                    job.Stage(count.Stage, count.IsWholeTarget).Add(count.State, count.Count);
                }
            }
        }

        OnChanged();
    }

    /// <summary>
    /// Forgets every job, e.g. when the library closes.
    /// </summary>
    public void Clear()
    {
        lock (gate)
        {
            jobs.Clear();
        }

        OnChanged();
    }

    /// <summary>
    /// Counts new work items of a job. A job that isn't tracked yet, e.g. a new one or a finished one that gets more
    /// work, starts with these items.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="workItems">The new work items, queued.</param>
    public void AddWork(ProcessingJob job, IEnumerable<WorkItem> workItems)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(workItems);

        lock (gate)
        {
            var now = clock.GetUtcNow();
            var counters = Find(job.Id);
            if (counters is null)
            {
                if (jobs.Count == 0)
                {
                    // Measure the throughput of this burst of work, not since the library opened.
                    overall = new Throughput(now);
                }

                counters = new JobCounters(job, now);
                jobs.Add(counters);
            }

            foreach (var workItem in workItems)
            {
                counters.Stage(workItem.Stage, workItem.ItemId is null).Add(WorkItemState.Queued, 1);
            }
        }

        OnChanged();
    }

    /// <summary>
    /// Takes back work items counted by <see cref="AddWork"/> that weren't stored after all. A job left with no work
    /// items is forgotten.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="workItems">The work items.</param>
    public void RemoveWork(Id<ProcessingJob> jobId, IEnumerable<WorkItem> workItems)
    {
        ArgumentNullException.ThrowIfNull(workItems);

        lock (gate)
        {
            if (Find(jobId) is not { } counters)
            {
                return;
            }

            foreach (var workItem in workItems)
            {
                counters.Stage(workItem.Stage, workItem.ItemId is null).Add(WorkItemState.Queued, -1);
            }

            if (counters.IsEmpty)
            {
                jobs.Remove(counters);
            }
        }

        OnChanged();
    }

    /// <summary>
    /// Counts a work item that stopped running, and forgets its job when the job is finished.
    /// </summary>
    /// <param name="workItem">The work item in its new state: completed, failed, cancelled, or queued again.</param>
    /// <param name="job">Its job after the change.</param>
    public void Finished(WorkItem workItem, ProcessingJob job)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(job);

        lock (gate)
        {
            if (Find(job.Id) is not { } counters)
            {
                return;
            }

            var stage = counters.Stage(workItem.Stage, workItem.ItemId is null);
            switch (workItem.State)
            {
                case WorkItemState.Completed or WorkItemState.Failed:
                    var now = clock.GetUtcNow();
                    // Already part of the total since it was queued.
                    stage.Add(workItem.State, 1);
                    stage.Add(WorkItemState.Queued, -1);
                    counters.Throughput.Record(now);
                    overall.Record(now);
                    break;
                case WorkItemState.Cancelled:
                    // Pending work that won't run isn't counted.
                    stage.Add(WorkItemState.Queued, -1);
                    break;
            }

            if (ProcessingJobStateTransitions.IsFinished(job.State))
            {
                jobs.Remove(counters);
            }
        }

        OnChanged();
    }

    /// <summary>
    /// Forgets a job, e.g. when it's cancelled.
    /// </summary>
    /// <param name="jobId">The job.</param>
    public void RemoveJob(Id<ProcessingJob> jobId)
    {
        lock (gate)
        {
            if (Find(jobId) is not { } counters)
            {
                return;
            }

            jobs.Remove(counters);
        }

        OnChanged();
    }

    private JobCounters? Find(Id<ProcessingJob> jobId) => jobs.Find(job => job.Job.Id == jobId);

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The counters of one job.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="since">When tracking started.</param>
    private sealed class JobCounters(ProcessingJob job, DateTimeOffset since)
    {
        private readonly List<StageCounters> stages = [];

        public ProcessingJob Job { get; } = job;

        public Throughput Throughput { get; } = new(since);

        public bool IsEmpty => stages.All(stage => stage.Total == 0);

        public StageCounters Stage(string stage, bool isWholeTarget)
        {
            var counters = stages.Find(existing => existing.Stage == stage);
            if (counters is null)
            {
                counters = new StageCounters(stage, isWholeTarget);
                stages.Add(counters);
            }

            return counters;
        }

        public JobProgress ToProgress(DateTimeOffset now)
        {
            var progress = new JobProgress(
                Job.Id,
                Job.Type,
                Job.Description,
                [.. stages.Where(stage => stage.Total > 0).Select(stage => new StageProgress(stage.Stage, stage.Total, stage.Done, stage.Failed, stage.IsWholeTarget))],
                null);
            return progress.IsTotalGrowing ? progress : progress with { TimeRemaining = Throughput.Estimate(progress.Pending, now) };
        }
    }

    /// <summary>
    /// The counters of one stage of a job.
    /// </summary>
    /// <param name="stage">Stage key.</param>
    /// <param name="isWholeTarget">Whether the stage runs for the job's whole target.</param>
    private sealed class StageCounters(string stage, bool isWholeTarget)
    {
        public string Stage { get; } = stage;

        public bool IsWholeTarget { get; } = isWholeTarget;

        public int Total { get; private set; }

        public int Done { get; private set; }

        public int Failed { get; private set; }

        /// <summary>
        /// Counts work items in a state: queued and running ones are only part of the total.
        /// </summary>
        public void Add(WorkItemState state, int count)
        {
            switch (state)
            {
                case WorkItemState.Queued or WorkItemState.Running:
                    Total += count;
                    break;
                case WorkItemState.Completed:
                    Done += count;
                    Total += count;
                    break;
                case WorkItemState.Failed:
                    Failed += count;
                    Total += count;
                    break;
            }
        }
    }

    /// <summary>
    /// Work items finished per second over the last <see cref="ThroughputWindow"/>.
    /// </summary>
    /// <param name="since">When measuring started.</param>
    private sealed class Throughput(DateTimeOffset since)
    {
        private readonly List<(long Second, int Count)> buckets = [];
        private int finished;

        public void Record(DateTimeOffset now)
        {
            var second = now.ToUnixTimeSeconds();
            if (buckets.Count > 0 && buckets[^1].Second == second)
            {
                buckets[^1] = (second, buckets[^1].Count + 1);
            }
            else
            {
                buckets.Add((second, 1));
            }

            finished++;
            Trim(now);
        }

        public TimeSpan? Estimate(int pending, DateTimeOffset now)
        {
            var elapsed = now - since;
            if (pending <= 0 || finished < MinimumFinishedForEstimate || elapsed < MinimumElapsedForEstimate)
            {
                return null;
            }

            Trim(now);
            var window = elapsed < ThroughputWindow ? elapsed : ThroughputWindow;
            var cutoff = (now - window).ToUnixTimeSeconds();
            var count = buckets.Where(bucket => bucket.Second >= cutoff).Sum(bucket => bucket.Count);
            return count == 0 ? null : TimeSpan.FromSeconds(pending * window.TotalSeconds / count);
        }

        private void Trim(DateTimeOffset now)
        {
            var cutoff = (now - ThroughputWindow).ToUnixTimeSeconds();
            var stale = buckets.FindIndex(bucket => bucket.Second >= cutoff);
            buckets.RemoveRange(0, stale < 0 ? buckets.Count : stale);
        }
    }
}
