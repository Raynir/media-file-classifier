namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// What the user owns of one item's metadata at a moment (E07-S03-T03): the user's field values, user tags, tombstones
/// and the new AI suggestions on tags. Taken before a bulk edit and written back by Undo. It is immutable and holds
/// no AI runs or file values: the bulk edit doesn't touch them.
/// </summary>
/// <param name="UserValues">The user-owned field values, with their suggestion flags.</param>
/// <param name="UserTags">The tags the user wrote or accepted.</param>
/// <param name="Rejections">The tag tombstones.</param>
/// <param name="TagSuggestions">The new AI suggestions on tags.</param>
public sealed record UserLayerSnapshot(
    IReadOnlyList<UserFieldValue> UserValues,
    IReadOnlyList<TagAssignment> UserTags,
    IReadOnlyList<TagRejection> Rejections,
    IReadOnlyList<TagSuggestion> TagSuggestions);
