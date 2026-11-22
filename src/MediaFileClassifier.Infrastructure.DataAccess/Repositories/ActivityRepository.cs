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

                // The newest MaxEntries stay. The oldest beyond them are found through the time index, not by counting
                // the whole table, and are normally none or one.
                var oldest = await db.ActivityEvents
                    .OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.Id)
                    .Skip(ActivityRetention.MaxEntries)
                    .Select(row => row.Id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                if (oldest.Count > 0)
                {
                    await db.ActivityEvents.Where(row => oldest.Contains(row.Id)).ExecuteDeleteAsync(token).ConfigureAwait(false);
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
