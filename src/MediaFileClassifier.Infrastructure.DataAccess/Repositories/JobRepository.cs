using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores processing jobs and work items. Reads use their own connection; writes go through the writer (D072), and
/// each change that touches a work item and its job is one write.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class JobRepository(LibraryConnectionFactory connections, LibraryWriter writer) : IJobRepository
{
    /// <inheritdoc />
    public async Task AddJobAsync(ProcessingJob job, IReadOnlyCollection<WorkItem> workItems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(workItems);

        var first = workItems.Take(MediaItemRepository.BatchSize).ToList();
        await writer.ExecuteAsync(
            (db, token) =>
            {
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                db.ProcessingJobs.Add(job);
                db.WorkItems.AddRange(first);
                return db.SaveChangesAsync(token);
            },
            cancellationToken).ConfigureAwait(false);

        // The first batch may already run and finish the job before the rest is written, so the rest is added like
        // later work, which queues a finished job again.
        if (workItems.Count > first.Count)
        {
            await AddWorkItemsAsync(job.Id, [.. workItems.Skip(first.Count)], job.CreatedAt, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> AddWorkItemsAsync(
        Id<ProcessingJob> jobId,
        IReadOnlyCollection<WorkItem> workItems,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItems);

        foreach (var batch in workItems.Chunk(MediaItemRepository.BatchSize))
        {
            var added = await writer.ExecuteAsync(
                async (db, token) =>
                {
                    var job = await db.ProcessingJobs.AsTracking().SingleOrDefaultAsync(job => job.Id == jobId, token).ConfigureAwait(false);
                    if (job is null || job.State == ProcessingJobState.Cancelled)
                    {
                        return false;
                    }

                    if (ProcessingJobStateTransitions.IsFinished(job.State))
                    {
                        db.Entry(job).CurrentValues.SetValues(job.ChangeState(ProcessingJobState.Queued, now));
                    }

                    db.WorkItems.AddRange(batch);
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (!added)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public Task<ProcessingJob?> GetJobAsync(Id<ProcessingJob> id, CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => db.ProcessingJobs.SingleOrDefaultAsync(job => job.Id == id, token), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProcessingJob>> GetUnfinishedJobsAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<ProcessingJob>>(
            async (db, token) => await db.ProcessingJobs
                .Where(job => job.State != ProcessingJobState.Completed && job.State != ProcessingJobState.Failed && job.State != ProcessingJobState.Cancelled)
                .OrderBy(job => job.CreatedAt)
                .ThenBy(job => job.Id)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItem>> GetQueuedAsync(
        ResourcePool pool,
        int limit,
        IReadOnlyCollection<Id<WorkItem>> excluding,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentNullException.ThrowIfNull(excluding);

        var excluded = excluding.ToList();
        return connections.ReadAsync<IReadOnlyList<WorkItem>>(
            async (db, token) => await db.WorkItems
                .Where(item => item.State == WorkItemState.Queued && item.Pool == pool && !excluded.Contains(item.Id))
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Take(limit)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItem>> GetQueuedForItemsAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var ids = itemIds.Select(id => (Id<MediaItem>?)id).ToList();
        return connections.ReadAsync<IReadOnlyList<WorkItem>>(
            async (db, token) => await db.WorkItems
                .Where(item => item.State == WorkItemState.Queued && ids.Contains(item.ItemId))
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<WorkItem>>(
            async (db, token) => await db.WorkItems
                .Where(item => item.JobId == jobId)
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync(
            (db, token) => db.WorkItems.CountAsync(item => item.State == WorkItemState.Queued || item.State == WorkItemState.Running, token),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItemCount>> CountUnfinishedWorkAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<WorkItemCount>>(
            async (db, token) =>
            {
                var groups = await db.WorkItems
                    .Where(item => db.ProcessingJobs.Any(job => job.Id == item.JobId
                        && job.State != ProcessingJobState.Completed && job.State != ProcessingJobState.Failed && job.State != ProcessingJobState.Cancelled))
                    .GroupBy(item => new { item.JobId, item.Stage, item.State, IsWholeTarget = item.ItemId == null })
                    .Select(group => new { group.Key.JobId, group.Key.Stage, group.Key.State, group.Key.IsWholeTarget, Count = group.Count() })
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                return [.. groups.Select(group => new WorkItemCount(group.JobId, group.Stage, group.State, group.IsWholeTarget, group.Count))];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<WorkItem?> StartAsync(WorkItem workItem, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return writer.ExecuteAsync(
            async (db, token) =>
            {
                var stored = await db.WorkItems.AsTracking().SingleOrDefaultAsync(item => item.Id == workItem.Id, token).ConfigureAwait(false);
                if (stored is null || stored.State != WorkItemState.Queued)
                {
                    return null;
                }

                var running = stored.ChangeState(WorkItemState.Running, now);
                db.Entry(stored).CurrentValues.SetValues(running);
                var job = await db.ProcessingJobs.AsTracking().SingleAsync(job => job.Id == workItem.JobId, token).ConfigureAwait(false);
                if (job.State == ProcessingJobState.Queued)
                {
                    db.Entry(job).CurrentValues.SetValues(job.ChangeState(ProcessingJobState.Running, now));
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return running;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ProcessingJob> FinishAsync(WorkItem workItem, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return writer.ExecuteAsync(
            async (db, token) =>
            {
                var stored = await db.WorkItems.AsTracking().SingleAsync(item => item.Id == workItem.Id, token).ConfigureAwait(false);
                db.Entry(stored).CurrentValues.SetValues(workItem);
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                var job = await db.ProcessingJobs.AsTracking().SingleAsync(job => job.Id == workItem.JobId, token).ConfigureAwait(false);
                var states = await db.WorkItems
                    .Where(item => item.JobId == job.Id)
                    .GroupBy(item => item.State)
                    .Select(group => group.Key)
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                if (job.State == ProcessingJobState.Running && !states.Any(WorkItemStateTransitions.IsPending))
                {
                    var finished = states.Contains(WorkItemState.Failed) && !states.Contains(WorkItemState.Completed)
                        ? job.ChangeState(ProcessingJobState.Failed, now, "Every work item failed.")
                        : job.ChangeState(ProcessingJobState.Completed, now);
                    db.Entry(job).CurrentValues.SetValues(finished);
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                    return finished;
                }

                return job;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CancelJobAsync(Id<ProcessingJob> jobId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) =>
            {
                var job = await db.ProcessingJobs.AsTracking().SingleOrDefaultAsync(job => job.Id == jobId, token).ConfigureAwait(false);
                if (job is null || ProcessingJobStateTransitions.IsFinished(job.State))
                {
                    return false;
                }

                db.Entry(job).CurrentValues.SetValues(job.ChangeState(ProcessingJobState.Cancelled, now));
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                await db.WorkItems
                    .Where(item => item.JobId == jobId && item.State == WorkItemState.Queued)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(item => item.State, WorkItemState.Cancelled).SetProperty(item => item.FinishedAt, now),
                        token)
                    .ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> RequeueRunningAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) =>
            {
                var count = await db.WorkItems
                    .Where(item => item.State == WorkItemState.Running)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, WorkItemState.Queued), token)
                    .ConfigureAwait(false);
                var running = await db.ProcessingJobs.AsTracking().Where(job => job.State == ProcessingJobState.Running).ToListAsync(token).ConfigureAwait(false);
                foreach (var job in running)
                {
                    db.Entry(job).CurrentValues.SetValues(job.ChangeState(ProcessingJobState.Queued, now));
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return count;
            },
            cancellationToken);
}
