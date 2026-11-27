using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// The duplicate resolution log of an open library (E12-S04): one entry per resolved or ignored group, with what
/// happened to each member and the paths of the files moved to the trash. Entries are kept forever; they outlive the
/// groups, which dissolve once the other members are removed (D578).
/// </summary>
public interface IDuplicateResolutionRepository
{
    /// <summary>
    /// Raised after a write that added or changed entries has committed, on a thread-pool thread.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Adds an entry and, in the same transaction, sets the group's state.
    /// </summary>
    /// <param name="resolution">The entry.</param>
    /// <param name="members">What happens to each member.</param>
    /// <param name="groupState">The state to give the group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether it was added: <c>false</c> when the group no longer exists, and then nothing is written.</returns>
    Task<bool> AddAsync(DuplicateResolution resolution, IReadOnlyList<DuplicateResolutionMember> members, DuplicateGroupState groupState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the actions and reasons of members of an entry, e.g. once the trash has answered.
    /// </summary>
    /// <param name="members">The members, matched by entry and item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task UpdateMembersAsync(IReadOnlyList<DuplicateResolutionMember> members, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an entry with its members.
    /// </summary>
    /// <param name="id">The entry's ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entry, or <c>null</c> if there's none with the ID.</returns>
    Task<DuplicateResolutionEntry?> GetAsync(Id<DuplicateResolution> id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of entries with their members, newest first.
    /// </summary>
    /// <param name="offset">Entries to skip.</param>
    /// <param name="limit">Most entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entries.</returns>
    Task<IReadOnlyList<DuplicateResolutionEntry>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count.</returns>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an entry undone and, in the same transaction, sets its group's state if the group still exists.
    /// </summary>
    /// <param name="id">The entry's ID.</param>
    /// <param name="groupState">The state to give the group, or <c>null</c> to leave it.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the entry existed and wasn't undone before.</returns>
    Task<bool> MarkUndoneAsync(Id<DuplicateResolution> id, DuplicateGroupState? groupState, DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>
/// A resolution log entry with its members.
/// </summary>
/// <param name="Resolution">The entry.</param>
/// <param name="Members">Its members, the keeper first.</param>
public sealed record DuplicateResolutionEntry(DuplicateResolution Resolution, IReadOnlyList<DuplicateResolutionMember> Members);
