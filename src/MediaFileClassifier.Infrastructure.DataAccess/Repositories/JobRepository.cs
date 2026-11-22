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
    public async Task AddJobAsync(ProcessingJob job, IReadOnlyCollection<WorkItem> workItems, Action<IReadOnlyCollection<WorkItem>>? stored, CancellationToken cancellationToken = default)
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
        stored?.Invoke(first);

        // The first batch may already run and finish the job before the rest is written, so the rest is added like
        // later work, which queues a finished job again.
        if (workItems.Count > first.Count)
        {
            await AddWorkItemsAsync(job.Id, [.. workItems.Skip(first.Count)], job.CreatedAt, stored, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> AddWorkItemsAsync(
        Id<ProcessingJob> jobId,
        IReadOnlyCollection<WorkItem> workItems,
        DateTimeOffset now,
        Action<IReadOnlyCollection<WorkItem>>? stored,
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

                    // New work wakes a finished job, and one waiting for a model: the new work may not need it.
                    if (ProcessingJobStateTransitions.IsFinished(job.State) || job.State == ProcessingJobState.WaitingForModel)
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

            stored?.Invoke(batch);
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
    public Task<IReadOnlyList<ProcessingJob>> GetRecentJobsAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return connections.ReadAsync<IReadOnlyList<ProcessingJob>>(
            async (db, token) => await db.ProcessingJobs
                .Where(job => job.State == ProcessingJobState.Completed || job.State == ProcessingJobState.Failed || job.State == ProcessingJobState.Cancelled)
                .OrderByDescending(job => job.FinishedAt)
                .ThenByDescending(job => job.Id)
                .Take(limit)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItemCount>> CountWorkAsync(IReadOnlyCollection<Id<ProcessingJob>> jobIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        var ids = jobIds.ToList();
        return connections.ReadAsync<IReadOnlyList<WorkItemCount>>(
            async (db, token) =>
            {
                var groups = await db.WorkItems
                    .Where(item => ids.Contains(item.JobId))
                    .GroupBy(item => new { item.JobId, item.Stage, item.State, IsWholeTarget = item.ItemId == null })
                    .Select(group => new { group.Key.JobId, group.Key.Stage, group.Key.State, group.Key.IsWholeTarget, Count = group.Count() })
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                return [.. groups.Select(group => new WorkItemCount(group.JobId, group.Stage, group.State, group.IsWholeTarget, group.Count))];
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Id<ProcessingJob>, int>> CountFailuresAsync(IReadOnlyCollection<Id<ProcessingJob>> jobIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        var ids = jobIds.ToList();
        return connections.ReadAsync<IReadOnlyDictionary<Id<ProcessingJob>, int>>(
            async (db, token) =>
            {
                var counts = await db.WorkItems
                    .Where(item => item.State == WorkItemState.Failed && ids.Contains(item.JobId))
                    .Where(item => item.ItemId == null || db.MediaItems.Any(media => media.Id == item.ItemId && media.Status == MediaItemStatus.Error))
                    .GroupBy(item => item.JobId)
                    .Select(group => new { JobId = group.Key, Count = group.Count() })
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                return counts.ToDictionary(count => count.JobId, count => count.Count);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItem>> GetFailedWorkItemsAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<WorkItem>>(
            async (db, token) => await db.WorkItems
                .Where(item => item.JobId == jobId && item.State == WorkItemState.Failed)
                .Where(item => item.ItemId == null || db.MediaItems.Any(media => media.Id == item.ItemId && media.Status == MediaItemStatus.Error))
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItem>> GetQueuedAsync(
        ResourcePool pool,
        int limit,
        IReadOnlyCollection<Id<WorkItem>> excluding,
        IReadOnlyCollection<string>? excludingStages = null,
        DateTimeOffset? dueBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentNullException.ThrowIfNull(excluding);

        var excluded = excluding.ToList();
        var excludedStages = excludingStages?.ToList() ?? [];
        return connections.ReadAsync<IReadOnlyList<WorkItem>>(
            async (db, token) => await db.WorkItems
                .Where(item => item.State == WorkItemState.Queued && item.Pool == pool && !excluded.Contains(item.Id) && !excludedStages.Contains(item.Stage))
                .Where(item => !db.ProcessingJobs.Any(job => job.Id == item.JobId && job.State == ProcessingJobState.Paused))
                .Where(item => dueBy == null || item.RetryAt == null || item.RetryAt <= dueBy)
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Take(limit)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetNextRetryAtAsync(DateTimeOffset after, CancellationToken cancellationToken = default) =>
        connections.ReadAsync(
            async (db, token) => await db.WorkItems
                .Where(item => item.State == WorkItemState.Queued && item.RetryAt > after && item.RetryAt != WorkItem.ParkedUntil)
                .OrderBy(item => item.RetryAt)
                .Select(item => item.RetryAt)
                .FirstOrDefaultAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task<int> ReleaseParkedAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) => await db.WorkItems
                .Where(item => item.State == WorkItemState.Queued && item.RetryAt == WorkItem.ParkedUntil)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RetryAt, now), token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItem>> GetQueuedForItemsAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var ids = itemIds.Select(id => (Id<MediaItem>?)id).ToList();
        return connections.ReadAsync<IReadOnlyList<WorkItem>>(
            async (db, token) => await db.WorkItems
                .Where(item => item.State == WorkItemState.Queued && ids.Contains(item.ItemId))
                .Where(item => !db.ProcessingJobs.Any(job => job.Id == item.JobId && job.State == ProcessingJobState.Paused))
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
            (db, token) => db.WorkItems.CountAsync(
                item => item.State == WorkItemState.Running
                    || (item.State == WorkItemState.Queued
                        && db.ProcessingJobs.Any(job => job.Id == item.JobId && job.State != ProcessingJobState.WaitingForModel && job.State != ProcessingJobState.Paused)),
                token),
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

                var job = await db.ProcessingJobs.AsTracking().SingleAsync(job => job.Id == workItem.JobId, token).ConfigureAwait(false);
                if (job.State == ProcessingJobState.Paused)
                {
                    // Paused after the scheduler read the work: it stays queued until the job is resumed.
                    return null;
                }

                var running = stored.ChangeState(WorkItemState.Running, now);
                db.Entry(stored).CurrentValues.SetValues(running);
                if (job.State == ProcessingJobState.WaitingForModel)
                {
                    // The model arrived between the scheduler reading the work and starting it.
                    db.Entry(job).CurrentValues.SetValues(job.ChangeState(ProcessingJobState.Queued, now));
                }

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
                if (job.State is ProcessingJobState.Running or ProcessingJobState.Paused && !states.Any(WorkItemStateTransitions.IsPending))
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
    public async Task<bool> SetResultAsync(Id<ProcessingJob> jobId, string result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        return await writer.ExecuteAsync(
            async (db, token) => await db.ProcessingJobs.Where(job => job.Id == jobId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.Result, result), token).ConfigureAwait(false) > 0,
            cancellationToken).ConfigureAwait(false);
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
    public Task<bool> PauseJobAsync(Id<ProcessingJob> jobId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) =>
            {
                var job = await db.ProcessingJobs.AsTracking().SingleOrDefaultAsync(job => job.Id == jobId, token).ConfigureAwait(false);
                if (job is null || ProcessingJobStateTransitions.IsFinished(job.State) || job.State == ProcessingJobState.Paused)
                {
                    return false;
                }

                db.Entry(job).CurrentValues.SetValues(job.ChangeState(ProcessingJobState.Paused, now));
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> ResumeJobAsync(Id<ProcessingJob> jobId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) =>
            {
                var job = await db.ProcessingJobs.AsTracking().SingleOrDefaultAsync(job => job.Id == jobId, token).ConfigureAwait(false);
                if (job is null || job.State != ProcessingJobState.Paused)
                {
                    return false;
                }

                // Resumed while its work still runs (within the pause's grace): it's running, so it finishes when that
                // work does. A queued job with only running work would never finish.
                var queued = job.ChangeState(ProcessingJobState.Queued, now);
                var stillRunning = await db.WorkItems.AnyAsync(item => item.JobId == jobId && item.State == WorkItemState.Running, token).ConfigureAwait(false);
                db.Entry(job).CurrentValues.SetValues(stillRunning ? queued.ChangeState(ProcessingJobState.Running, now) : queued);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> ResumeAllJobsAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) =>
            {
                var jobs = await db.ProcessingJobs.AsTracking().Where(job => job.State == ProcessingJobState.Paused).ToListAsync(token).ConfigureAwait(false);
                foreach (var job in jobs)
                {
                    db.Entry(job).CurrentValues.SetValues(job.ChangeState(ProcessingJobState.Queued, now));
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return jobs.Count;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> PruneFinishedJobsAsync(int keepLast, DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepLast);

        // Everything happens in one write, so no other write can wake a job between choosing it and deleting it; the
        // state is checked in the delete as well, in case a job isn't what it was.
        return writer.ExecuteAsync(
            async (db, token) =>
            {
                var finished = db.ProcessingJobs.Where(job =>
                    (job.State == ProcessingJobState.Completed || job.State == ProcessingJobState.Failed || job.State == ProcessingJobState.Cancelled)
                    && job.FinishedAt != null);
                var beyondLimit = await finished
                    .OrderByDescending(job => job.FinishedAt)
                    .ThenByDescending(job => job.Id)
                    .Skip(keepLast)
                    .Select(job => job.Id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                var tooOld = await finished.Where(job => job.FinishedAt < olderThan).Select(job => job.Id).ToListAsync(token).ConfigureAwait(false);

                var deleted = 0;
                foreach (var batch in beyondLimit.Union(tooOld).Chunk(MediaItemRepository.BatchSize))
                {
                    deleted += await db.ProcessingJobs
                        .Where(job => batch.Contains(job.Id)
                            && (job.State == ProcessingJobState.Completed || job.State == ProcessingJobState.Failed || job.State == ProcessingJobState.Cancelled))
                        .ExecuteDeleteAsync(token)
                        .ConfigureAwait(false);
                }

                return deleted;
            },
            cancellationToken);
    }

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

    /// <inheritdoc />
    public Task<int> MarkWaitingForModelAsync(IReadOnlyCollection<string> stages, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count == 0)
        {
            return Task.FromResult(0);
        }

        var waiting = stages.ToList();
        return writer.ExecuteAsync(
            async (db, token) =>
            {
                var jobs = await db.ProcessingJobs.AsTracking()
                    .Where(job => job.State == ProcessingJobState.Queued || job.State == ProcessingJobState.Running)
                    .Where(job => db.WorkItems.Any(item => item.JobId == job.Id && item.State == WorkItemState.Queued && waiting.Contains(item.Stage)))
                    .Where(job => !db.WorkItems.Any(
                        item => item.JobId == job.Id
                            && (item.State == WorkItemState.Running || (item.State == WorkItemState.Queued && !waiting.Contains(item.Stage)))))
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                foreach (var job in jobs)
                {
                    db.Entry(job).CurrentValues.SetValues(job.ChangeState(ProcessingJobState.WaitingForModel, now));
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return jobs.Count;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ResumeWaitingForModelAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) =>
            {
                var jobs = await db.ProcessingJobs.AsTracking()
                    .Where(job => job.State == ProcessingJobState.WaitingForModel)
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                foreach (var job in jobs)
                {
                    db.Entry(job).CurrentValues.SetValues(job.ChangeState(ProcessingJobState.Queued, now));
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return jobs.Count;
            },
            cancellationToken);
}
