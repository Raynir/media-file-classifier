using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores the duplicate resolution log (E12-S04). An entry and its group's new state are written in one writer
/// transaction, so the log never misses a state change and never records one that didn't happen.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
/// <param name="groups">The group repository, whose listeners hear about state changes made here.</param>
/// <param name="logger">Logs listeners of <see cref="Changed"/> that fail.</param>
internal sealed partial class DuplicateResolutionRepository(
    LibraryConnectionFactory connections,
    LibraryWriter writer,
    DuplicateGroupRepository groups,
    ILogger<DuplicateResolutionRepository> logger) : IDuplicateResolutionRepository
{
    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task<bool> AddAsync(
        DuplicateResolution resolution,
        IReadOnlyList<DuplicateResolutionMember> members,
        DuplicateGroupState groupState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(members);
        if (members.Any(member => member.ResolutionId != resolution.Id))
        {
            throw new ArgumentException("Every member must belong to the entry.", nameof(members));
        }

        var added = await writer.ExecuteAsync(
            async (db, token) =>
            {
                var group = await db.DuplicateGroups.SingleOrDefaultAsync(group => group.Id == resolution.GroupId, token).ConfigureAwait(false);
                if (group is null)
                {
                    return false;
                }

                group.ChangeState(groupState, resolution.CreatedAt);
                db.DuplicateResolutions.Add(resolution);
                db.DuplicateResolutionMembers.AddRange(members);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
        if (added)
        {
            groups.RaiseChanged();
            RaiseChanged();
        }

        return added;
    }

    /// <inheritdoc />
    public async Task UpdateMembersAsync(IReadOnlyList<DuplicateResolutionMember> members, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count == 0)
        {
            return;
        }

        await writer.ExecuteAsync(
            async (db, token) =>
            {
                foreach (var member in members)
                {
                    await db.DuplicateResolutionMembers
                        .Where(row => row.ResolutionId == member.ResolutionId && row.ItemId == member.ItemId)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(row => row.Action, member.Action)
                                .SetProperty(row => row.Reason, member.Reason),
                            token)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    /// <inheritdoc />
    public Task<DuplicateResolutionEntry?> GetAsync(Id<DuplicateResolution> id, CancellationToken cancellationToken = default) =>
        connections.ReadAsync(
            async (db, token) =>
            {
                var resolution = await db.DuplicateResolutions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token).ConfigureAwait(false);
                return resolution is null ? null : (await WithMembersAsync(db, [resolution], token).ConfigureAwait(false))[0];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<DuplicateResolutionEntry>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return connections.ReadAsync<IReadOnlyList<DuplicateResolutionEntry>>(
            async (db, token) =>
            {
                var page = await db.DuplicateResolutions.AsNoTracking()
                    .OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
                    .Skip(offset).Take(limit)
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                return await WithMembersAsync(db, page, token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => db.DuplicateResolutions.CountAsync(token), cancellationToken);

    /// <inheritdoc />
    public async Task<bool> MarkUndoneAsync(Id<DuplicateResolution> id, DuplicateGroupState? groupState, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var (marked, groupChanged) = await writer.ExecuteAsync(
            async (db, token) =>
            {
                var resolution = await db.DuplicateResolutions.SingleOrDefaultAsync(row => row.Id == id, token).ConfigureAwait(false);
                if (resolution is null || !resolution.MarkUndone(now))
                {
                    return (false, false);
                }

                var changed = false;
                if (groupState is { } state
                    && await db.DuplicateGroups.SingleOrDefaultAsync(group => group.Id == resolution.GroupId, token).ConfigureAwait(false) is { } group
                    && DuplicateGroupStateTransitions.IsAllowed(group.State, state))
                {
                    changed = group.ChangeState(state, now);
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return (true, changed);
            },
            cancellationToken).ConfigureAwait(false);
        if (groupChanged)
        {
            groups.RaiseChanged();
        }

        if (marked)
        {
            RaiseChanged();
        }

        return marked;
    }

    private static async Task<List<DuplicateResolutionEntry>> WithMembersAsync(LibraryDbContext db, List<DuplicateResolution> resolutions, CancellationToken cancellationToken)
    {
        List<Id<DuplicateResolution>> ids = [.. resolutions.Select(resolution => resolution.Id)];
        var members = (await db.DuplicateResolutionMembers.AsNoTracking()
                .Where(member => ids.Contains(member.ResolutionId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToLookup(member => member.ResolutionId);
        return
        [
            .. resolutions.Select(resolution => new DuplicateResolutionEntry(
                resolution,
                [.. members[resolution.Id].OrderBy(member => member.Action != DuplicateMemberAction.Kept).ThenBy(member => member.Path, StringComparer.OrdinalIgnoreCase)])),
        ];
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            LogListenerFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A listener of the duplicate resolution log failed")]
    private partial void LogListenerFailed(Exception exception);
}
