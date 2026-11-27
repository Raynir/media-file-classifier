using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// What the user did with a duplicate group: an entry of the resolution log (E12-S04). The group ID is a plain value:
/// removing the other members dissolves the group (D578), and the entry outlives it.
/// </summary>
public sealed class DuplicateResolution
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateResolution"/> class.
    /// </summary>
    /// <param name="id">The entry's ID.</param>
    /// <param name="groupId">The group that was resolved or ignored.</param>
    /// <param name="groupType">The group's type.</param>
    /// <param name="kind">Resolved or ignored.</param>
    /// <param name="keeperId">The member that was kept; none for an ignored group.</param>
    /// <param name="mergedMetadata">Whether the other members' metadata was merged into the keeper.</param>
    /// <param name="removedFromLibrary">Whether the other members were removed from the library.</param>
    /// <param name="trashRequested">Whether the user asked to move the other members' files to the trash.</param>
    /// <param name="createdAt">When it was done.</param>
    /// <param name="undoneAt">When it was undone, if it was.</param>
    /// <param name="merge">What the merge changed on the keeper, for undo; none when nothing changed.</param>
    public DuplicateResolution(
        Id<DuplicateResolution> id,
        Id<DuplicateGroup> groupId,
        DuplicateGroupType groupType,
        DuplicateResolutionKind kind,
        Id<MediaItem>? keeperId,
        bool mergedMetadata,
        bool removedFromLibrary,
        bool trashRequested,
        DateTimeOffset createdAt,
        DateTimeOffset? undoneAt,
        MetadataMergeRecord? merge)
    {
        if (kind == DuplicateResolutionKind.Resolved && keeperId is null)
        {
            throw new ArgumentException("A resolution needs its keeper.", nameof(keeperId));
        }

        if (trashRequested && !removedFromLibrary)
        {
            throw new ArgumentException("Files are only moved to the trash when their items are removed from the library.", nameof(trashRequested));
        }

        Id = id;
        GroupId = groupId;
        GroupType = groupType;
        Kind = kind;
        KeeperId = keeperId;
        MergedMetadata = mergedMetadata;
        RemovedFromLibrary = removedFromLibrary;
        TrashRequested = trashRequested;
        CreatedAt = createdAt;
        UndoneAt = undoneAt;
        Merge = merge;
    }

    /// <summary>Gets the entry's ID.</summary>
    public Id<DuplicateResolution> Id { get; }

    /// <summary>Gets the group that was resolved or ignored.</summary>
    public Id<DuplicateGroup> GroupId { get; }

    /// <summary>Gets the group's type.</summary>
    public DuplicateGroupType GroupType { get; }

    /// <summary>Gets whether the group was resolved or ignored.</summary>
    public DuplicateResolutionKind Kind { get; }

    /// <summary>Gets the member that was kept; none for an ignored group.</summary>
    public Id<MediaItem>? KeeperId { get; }

    /// <summary>Gets a value indicating whether the other members' metadata was merged into the keeper.</summary>
    public bool MergedMetadata { get; }

    /// <summary>Gets a value indicating whether the other members were removed from the library.</summary>
    public bool RemovedFromLibrary { get; }

    /// <summary>Gets a value indicating whether the user asked to move the other members' files to the trash.</summary>
    public bool TrashRequested { get; }

    /// <summary>Gets when it was done.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets when it was undone, if it was.</summary>
    public DateTimeOffset? UndoneAt { get; private set; }

    /// <summary>Gets what the merge changed on the keeper; none when nothing changed.</summary>
    public MetadataMergeRecord? Merge { get; }

    /// <summary>Gets a value indicating whether it was undone.</summary>
    public bool IsUndone => UndoneAt is not null;

    /// <summary>
    /// Starts an entry for a group the user resolved.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="keeperId">The member that is kept.</param>
    /// <param name="options">What the user chose.</param>
    /// <param name="merge">What the merge changed on the keeper.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The entry.</returns>
    public static DuplicateResolution Resolved(DuplicateGroup group, Id<MediaItem> keeperId, DuplicateResolutionOptions options, MetadataMergeRecord? merge, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(options);

        return new(
            Id<DuplicateResolution>.New(),
            group.Id,
            group.Type,
            DuplicateResolutionKind.Resolved,
            keeperId,
            options.MergeMetadata,
            options.RemoveFromLibrary,
            options.MoveToTrash,
            now,
            null,
            merge);
    }

    /// <summary>
    /// Starts an entry for a group the user marked as not duplicates.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The entry.</returns>
    public static DuplicateResolution Ignored(DuplicateGroup group, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(group);

        return new(Id<DuplicateResolution>.New(), group.Id, group.Type, DuplicateResolutionKind.Ignored, null, false, false, false, now, null, null);
    }

    /// <summary>
    /// Marks the entry undone.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <returns>Whether it wasn't undone before.</returns>
    public bool MarkUndone(DateTimeOffset now)
    {
        if (UndoneAt is not null)
        {
            return false;
        }

        UndoneAt = now;
        return true;
    }
}

/// <summary>
/// Whether a log entry resolved a group or marked it as not duplicates.
/// </summary>
public enum DuplicateResolutionKind
{
    /// <summary>A keeper was chosen.</summary>
    Resolved = 0,

    /// <summary>The group was marked as not duplicates.</summary>
    Ignored = 1,
}

/// <summary>
/// What happened to one member of a resolved or ignored group.
/// </summary>
public enum DuplicateMemberAction
{
    /// <summary>The member was the keeper.</summary>
    Kept = 0,

    /// <summary>Removed from the library; the file stays.</summary>
    Removed = 1,

    /// <summary>Removed from the library and its file moved to the system trash.</summary>
    Trashed = 2,

    /// <summary>Removed from the library; moving the file to the trash was refused, so the file stays.</summary>
    TrashSkipped = 3,

    /// <summary>Removed from the library; moving the file to the trash failed, so the file stays.</summary>
    TrashFailed = 4,

    /// <summary>The group was marked as not duplicates.</summary>
    Ignored = 5,

    /// <summary>Left in the library: the user resolved the group without removing the others.</summary>
    Retained = 6,

    /// <summary>
    /// Removed from the library and its file handed to the trash, with no answer recorded yet: the app stopped while
    /// moving it. The file may or may not be in the trash.
    /// </summary>
    TrashRequested = 7,
}

/// <summary>
/// One member of a log entry, with the path its file had then.
/// </summary>
/// <param name="ResolutionId">The entry.</param>
/// <param name="ItemId">The member's item.</param>
/// <param name="Path">The full path of the member's file at the time.</param>
/// <param name="Action">What happened to it.</param>
/// <param name="Reason">Why moving it to the trash was refused or failed.</param>
public sealed record DuplicateResolutionMember(
    Id<DuplicateResolution> ResolutionId,
    Id<MediaItem> ItemId,
    string Path,
    DuplicateMemberAction Action,
    string? Reason = null);

/// <summary>
/// What the user chose in the resolve dialog (D592): merge metadata (on by default), remove the others from the library
/// (on by default) and move the others' files to the trash (off by default; needs removal, D024).
/// </summary>
/// <param name="MergeMetadata">Merge the others' metadata into the keeper.</param>
/// <param name="RemoveFromLibrary">Remove the others from the library.</param>
/// <param name="MoveToTrash">Also move the others' files to the system trash.</param>
public sealed record DuplicateResolutionOptions(bool MergeMetadata = true, bool RemoveFromLibrary = true, bool MoveToTrash = false)
{
    /// <summary>Gets the defaults: merge and remove, no trash.</summary>
    public static DuplicateResolutionOptions Default { get; } = new();
}
