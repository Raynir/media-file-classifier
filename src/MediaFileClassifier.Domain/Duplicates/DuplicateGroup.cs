using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// Items found to be duplicates of each other, with the user's review state (E12-S01-T01). The members are
/// <see cref="DuplicateGroupMember"/> rows; a group always has two or more, and is deleted when it drops below two.
/// </summary>
/// <remarks>
/// Invariants: an <see cref="DuplicateGroupType.Exact"/> group has the content hash its members share, and the state
/// only changes as <see cref="DuplicateGroupStateTransitions"/> allows.
/// </remarks>
public sealed class DuplicateGroup
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateGroup"/> class from stored values.
    /// </summary>
    /// <param name="id">Group ID.</param>
    /// <param name="type">How the members were found.</param>
    /// <param name="state">The review state.</param>
    /// <param name="contentHash">The members' content hash; required for <see cref="DuplicateGroupType.Exact"/>.</param>
    /// <param name="createdAt">When the group was found.</param>
    /// <param name="stateChangedAt">When the state last changed.</param>
    public DuplicateGroup(Id<DuplicateGroup> id, DuplicateGroupType type, DuplicateGroupState state, UInt128? contentHash, DateTimeOffset createdAt, DateTimeOffset stateChangedAt)
    {
        if (type == DuplicateGroupType.Exact && contentHash is null)
        {
            throw new ArgumentException("An exact duplicate group needs its content hash.", nameof(contentHash));
        }

        Id = id;
        Type = type;
        State = state;
        ContentHash = contentHash;
        CreatedAt = createdAt;
        StateChangedAt = stateChangedAt;
    }

    /// <summary>
    /// Gets the group ID.
    /// </summary>
    public Id<DuplicateGroup> Id { get; }

    /// <summary>
    /// Gets how the members were found.
    /// </summary>
    public DuplicateGroupType Type { get; }

    /// <summary>
    /// Gets the review state.
    /// </summary>
    public DuplicateGroupState State { get; private set; }

    /// <summary>
    /// Gets the content hash the members share, for <see cref="DuplicateGroupType.Exact"/> groups.
    /// </summary>
    public UInt128? ContentHash { get; }

    /// <summary>
    /// Gets when the group was found.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets when the state last changed.
    /// </summary>
    public DateTimeOffset StateChangedAt { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the group is suggested to the user for review: only Open groups are.
    /// </summary>
    public bool IsSuggested => State == DuplicateGroupState.Open;

    /// <summary>
    /// Creates an Open group of exact copies.
    /// </summary>
    /// <param name="contentHash">The content hash the members share.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The new group.</returns>
    public static DuplicateGroup CreateExact(UInt128 contentHash, DateTimeOffset now) =>
        new(Id<DuplicateGroup>.New(), DuplicateGroupType.Exact, DuplicateGroupState.Open, contentHash, now, now);

    /// <summary>
    /// Changes the review state, e.g. when the user ignores or resolves the group.
    /// </summary>
    /// <param name="state">The new state.</param>
    /// <param name="now">Current time.</param>
    /// <returns><see langword="true"/> if the state changed; <see langword="false"/> if it already was <paramref name="state"/>.</returns>
    /// <exception cref="InvalidOperationException">The change isn't allowed.</exception>
    public bool ChangeState(DuplicateGroupState state, DateTimeOffset now)
    {
        if (!DuplicateGroupStateTransitions.IsAllowed(State, state))
        {
            throw new InvalidOperationException($"A duplicate group can't change from {State} to {state}.");
        }

        if (State == state)
        {
            return false;
        }

        State = state;
        StateChangedAt = now;
        return true;
    }

    /// <summary>
    /// Records that a new member joined: a Resolved or Ignored group opens again, so it's suggested again.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <returns><see langword="true"/> if the group was reopened.</returns>
    public bool MemberJoined(DateTimeOffset now) => ChangeState(DuplicateGroupState.Open, now);
}
