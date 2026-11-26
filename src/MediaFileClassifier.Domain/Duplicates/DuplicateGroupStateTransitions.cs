namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// Allowed changes of <see cref="DuplicateGroupState"/>. Changing to the current state is always allowed and changes nothing.
/// </summary>
/// <remarks>
/// <code>
/// Open     → Resolved | Ignored
/// Resolved → Open      (a new member joined, or the user reopened it)
/// Ignored  → Open      (a new member joined, or the user stopped ignoring it)
/// </code>
/// A Resolved or Ignored group goes through Open to reach the other state.
/// </remarks>
public static class DuplicateGroupStateTransitions
{
    /// <summary>
    /// Checks whether a group may change from one state to another.
    /// </summary>
    /// <param name="from">The current state.</param>
    /// <param name="to">The new state.</param>
    /// <returns><see langword="true"/> when the change is allowed.</returns>
    public static bool IsAllowed(DuplicateGroupState from, DuplicateGroupState to) =>
        from == to || from == DuplicateGroupState.Open || to == DuplicateGroupState.Open;
}
