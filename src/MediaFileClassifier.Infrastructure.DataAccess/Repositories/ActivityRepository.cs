using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores the activity log (E15-S04). An event is added and the oldest ones beyond the retention are dropped in one
/// write, so the log never holds more than <see cref="ActivityRetention.MaxEntries"/> events. Pages are read with a
/// skip and take over the time index, which is cheap for the few thousand events the retention allows.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class ActivityRepository(LibraryConnectionFactory connections, LibraryWriter writer) : IActivityRepository
{
    /// <inheritdoc />
    public Task AddAsync(ActivityEvent activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return writer.ExecuteAsync(
            async (db, token) =>
            {
                db.ActivityEvents.Add(activity);
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                // The newest MaxEntries stay. The last of them is found through the time index, and everything strictly
                // older goes in one statement, with no list of IDs to carry. Events of the same millisecond as that last
                // one all stay, so the log can hold a few more than MaxEntries until the next write trims them.
                var cutoff = await db.ActivityEvents
                    .OrderByDescending(row => row.OccurredAt)
                    .Skip(ActivityRetention.MaxEntries - 1)
                    .Select(row => (DateTimeOffset?)row.OccurredAt)
                    .FirstOrDefaultAsync(token)
                    .ConfigureAwait(false);
                if (cutoff is { } oldestKept)
                {
                    await db.ActivityEvents.Where(row => row.OccurredAt < oldestKept).ExecuteDeleteAsync(token).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ActivityEvent>> GetPageAsync(
        ActivityFilter filter,
        int skip,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        return connections.ReadAsync<IReadOnlyList<ActivityEvent>>(
            async (db, token) =>
            {
                return await Filtered(db, filter)
                    .OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.Id)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync(token)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(ActivityFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return connections.ReadAsync(
            async (db, token) => await Filtered(db, filter).CountAsync(token).ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ActivityPageResult> GetPageWithTotalAsync(
        ActivityFilter filter,
        int skip,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        return connections.ReadAsync(
            async (db, token) =>
            {
                // One read transaction, so a write between the two queries can't show in only one of them.
                await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                var events = await Filtered(db, filter)
                    .OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.Id)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                var total = await Filtered(db, filter).CountAsync(token).ConfigureAwait(false);
                return new ActivityPageResult(events, total);
            },
            cancellationToken);
    }

    private static IQueryable<ActivityEvent> Filtered(LibraryDbContext db, ActivityFilter filter)
    {
        var query = db.ActivityEvents.AsNoTracking();
        if (filter.Types is { } types)
        {
            var list = types.ToList();
            query = query.Where(row => list.Contains(row.Type));
        }

        if (filter.MinimumSeverity is { } minimum)
        {
            query = query.Where(row => row.Severity >= minimum);
        }

        return query;
    }
}
