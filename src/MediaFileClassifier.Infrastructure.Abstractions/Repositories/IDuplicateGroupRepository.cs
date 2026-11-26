using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the duplicate groups of an open library (E12-S01). Exact groups follow the items: the database queues every
/// content hash whose items were added, changed, deleted, or became Missing or Excluded (or came back), in the same
/// transaction as that change, and <see cref="ReconcileAsync"/> brings the groups of queued hashes up to date. Items
/// that are Missing or Excluded aren't members, and a group with fewer than two members is deleted.
/// </summary>
public interface IDuplicateGroupRepository
{
    /// <summary>
    /// Raised after groups were created, changed or deleted, on the writing thread, once per write.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Gets a group.
    /// </summary>
    /// <param name="id">Group ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The group, or <c>null</c> if there's none with the ID.</returns>
    Task<DuplicateGroup?> GetAsync(Id<DuplicateGroup> id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of groups, oldest first.
    /// </summary>
    /// <param name="state">The state to list, or <c>null</c> for every group.</param>
    /// <param name="offset">Number of groups to skip.</param>
    /// <param name="limit">Maximum number of groups to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The groups, by creation time and then ID.</returns>
    Task<IReadOnlyList<DuplicateGroup>> GetGroupsAsync(DuplicateGroupState? state, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the groups.
    /// </summary>
    /// <param name="state">The state to count, or <c>null</c> for every group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of groups.</returns>
    Task<int> CountAsync(DuplicateGroupState? state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the members of a group.
    /// </summary>
    /// <param name="groupId">The group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The members, in item ID order; empty if the group doesn't exist.</returns>
    Task<IReadOnlyList<DuplicateGroupMember>> GetMembersAsync(Id<DuplicateGroup> groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a group's state, e.g. when the user ignores it, as <see cref="DuplicateGroup.ChangeState"/> allows.
    /// </summary>
    /// <param name="id">Group ID.</param>
    /// <param name="state">The new state.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the state changed; <see langword="false"/> if it already was the state, or the group is gone.</returns>
    /// <exception cref="InvalidOperationException">The change isn't allowed.</exception>
    Task<bool> ChangeStateAsync(Id<DuplicateGroup> id, DuplicateGroupState state, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings the exact groups of up to <paramref name="limit"/> queued content hashes up to date, in one short write,
    /// and takes the hashes off the queue: a group is created for two or more member items, members are added and
    /// removed, a Resolved or Ignored group that gains a member opens again, and a group left with fewer than two
    /// members is deleted. Call it until it returns <see cref="DuplicateReconcileResult.Empty"/>.
    /// </summary>
    /// <param name="limit">Maximum number of hashes to take.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What changed.</returns>
    Task<DuplicateReconcileResult> ReconcileAsync(int limit, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues every content hash that two or more member items share, and every exact group's hash, so reconciling
    /// rebuilds all exact groups: the full detection pass (E12-S01-T03).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of hashes queued afterwards.</returns>
    Task<int> QueueAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the queued content hashes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of hashes waiting for <see cref="ReconcileAsync"/>.</returns>
    Task<int> CountQueuedAsync(CancellationToken cancellationToken = default);
}
