using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// Which members of a group a resolution removes and trashes. It is the one place that decides it, and it guarantees the
/// safety rules of E12-S04: the keeper is never removed or trashed, at least one member (the keeper) always stays, and
/// nothing is trashed unless the user opted in and the members are removed from the library too (D024).
/// </summary>
public sealed record DuplicateResolutionPlan
{
    private DuplicateResolutionPlan(Id<MediaItem> keeperId, IReadOnlyList<Id<MediaItem>> others, IReadOnlyList<Id<MediaItem>> toRemove, IReadOnlyList<Id<MediaItem>> toTrash)
    {
        KeeperId = keeperId;
        Others = others;
        ToRemove = toRemove;
        ToTrash = toTrash;
    }

    /// <summary>Gets the member that stays.</summary>
    public Id<MediaItem> KeeperId { get; }

    /// <summary>Gets every member but the keeper.</summary>
    public IReadOnlyList<Id<MediaItem>> Others { get; }

    /// <summary>Gets the members to remove from the library.</summary>
    public IReadOnlyList<Id<MediaItem>> ToRemove { get; }

    /// <summary>Gets the members whose files go to the system trash.</summary>
    public IReadOnlyList<Id<MediaItem>> ToTrash { get; }

    /// <summary>
    /// Plans a resolution.
    /// </summary>
    /// <param name="members">The group's members.</param>
    /// <param name="keeperId">The member to keep.</param>
    /// <param name="options">What the user chose.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">The keeper isn't a member, the group has fewer than two members, or trash was asked without removal.</exception>
    public static DuplicateResolutionPlan Create(IReadOnlyCollection<Id<MediaItem>> members, Id<MediaItem> keeperId, DuplicateResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(options);

        var distinct = members.Distinct().ToList();
        if (distinct.Count < 2)
        {
            throw new ArgumentException("A duplicate group has at least two members.", nameof(members));
        }

        if (!distinct.Contains(keeperId))
        {
            throw new ArgumentException("The keeper must be a member of the group.", nameof(keeperId));
        }

        if (options.MoveToTrash && !options.RemoveFromLibrary)
        {
            throw new ArgumentException("Files are only moved to the trash when their items are removed from the library.", nameof(options));
        }

        List<Id<MediaItem>> others = [.. distinct.Where(id => id != keeperId)];
        IReadOnlyList<Id<MediaItem>> toRemove = options.RemoveFromLibrary ? others : [];
        IReadOnlyList<Id<MediaItem>> toTrash = options.MoveToTrash ? others : [];
        return new DuplicateResolutionPlan(keeperId, others, toRemove, toTrash);
    }

    /// <summary>
    /// Gets what happens to a member before any file is moved: the keeper is kept, the others are removed, retained, or
    /// handed to the trash (until the trash answers).
    /// </summary>
    /// <param name="itemId">The member.</param>
    /// <returns>Its planned action.</returns>
    public DuplicateMemberAction PlannedAction(Id<MediaItem> itemId) =>
        itemId == KeeperId ? DuplicateMemberAction.Kept
        : ToTrash.Contains(itemId) ? DuplicateMemberAction.TrashRequested
        : ToRemove.Contains(itemId) ? DuplicateMemberAction.Removed
        : DuplicateMemberAction.Retained;
}
