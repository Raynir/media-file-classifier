using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Merges the user's metadata of duplicates into the item kept (E12-S04-T02, spec 1.8). Only the user layer moves:
/// <list type="bullet">
/// <item>User tags (written or accepted) are combined: the keeper gets every tag a member has, as a tag the user wrote.</item>
/// <item>User field values (written or accepted): the keeper's value wins. A field only members have takes the first
/// member's value, as a value the user wrote. Each other value is reported as a conflict, so the user can pick it.</item>
/// <item>AI values, AI tags, tag rejections and file values are not merged: the keeper keeps its own.</item>
/// </list>
/// Favorites and collection memberships are kept on the items, not in <see cref="ItemMetadata"/>; the use case merges
/// the favorite mark.
/// </summary>
public static class MetadataMerge
{
    /// <summary>
    /// Merges the members' user layer into the keeper's metadata.
    /// </summary>
    /// <param name="keeper">The keeper's metadata, changed in place.</param>
    /// <param name="members">The other members' metadata, in order of preference: a field only members have takes the
    /// first one's value. They aren't changed.</param>
    /// <param name="now">Current time.</param>
    /// <returns>What changed, and the conflicts.</returns>
    /// <exception cref="ArgumentException">A member is the keeper.</exception>
    public static MetadataMergeOutcome Apply(ItemMetadata keeper, IReadOnlyList<ItemMetadata> members, DateTimeOffset now) =>
        Apply(keeper, members, null, now);

    /// <summary>
    /// Merges the members' user layer into the keeper's metadata, taking the value the user picked for a conflict
    /// (E12-S04 resolve dialog). A choice counts only for a field in conflict and only with one of the members' values.
    /// </summary>
    /// <param name="keeper">The keeper's metadata, changed in place.</param>
    /// <param name="members">The other members' metadata, in order of preference.</param>
    /// <param name="choices">The value the user picked per conflicting field; none keeps the keeper's values.</param>
    /// <param name="now">Current time.</param>
    /// <returns>What changed, and the conflicts as they were before the choices.</returns>
    /// <exception cref="ArgumentException">A member is the keeper.</exception>
    public static MetadataMergeOutcome Apply(
        ItemMetadata keeper,
        IReadOnlyList<ItemMetadata> members,
        IReadOnlyDictionary<DescriptiveField, FieldValue>? choices,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(keeper);
        ArgumentNullException.ThrowIfNull(members);
        if (members.Any(member => member.ItemId == keeper.ItemId))
        {
            throw new ArgumentException("The keeper can't be merged into itself.", nameof(members));
        }

        var added = MergeTags(keeper, members, now);
        var copied = MergeFields(keeper, members, now, out var conflicts);
        List<DescriptiveField> chosen = [];
        foreach (var conflict in conflicts)
        {
            if (choices is not null
                && choices.TryGetValue(conflict.Field, out var choice)
                && conflict.Alternatives.Any(alternative => alternative.Value == choice))
            {
                keeper.EditValue(conflict.Field, choice, now);
                chosen.Add(conflict.Field);
            }
        }

        return new MetadataMergeOutcome(added, copied, conflicts) { ChosenFields = chosen };
    }

    private static List<TagName> MergeTags(ItemMetadata keeper, IReadOnlyList<ItemMetadata> members, DateTimeOffset now)
    {
        var keeperTags = UserTags(keeper).ToHashSet();
        var added = new List<TagName>();
        foreach (var tag in members.SelectMany(UserTags))
        {
            if (keeperTags.Add(tag))
            {
                keeper.AddTag(tag, now);
                added.Add(tag);
            }
        }

        return added;
    }

    private static List<DescriptiveField> MergeFields(
        ItemMetadata keeper,
        IReadOnlyList<ItemMetadata> members,
        DateTimeOffset now,
        out List<MetadataMergeConflict> conflicts)
    {
        conflicts = [];
        var copied = new List<DescriptiveField>();
        var fields = members.SelectMany(member => member.UserValues).Select(value => value.Field).Distinct().Order();
        foreach (var field in fields)
        {
            var values = members
                .Select(member => (member.ItemId, Value: member.GetUserValue(field)?.Value))
                .Where(candidate => candidate.Value is not null)
                .Select(candidate => new MemberFieldValue(candidate.ItemId, candidate.Value!))
                .ToList();

            var keeperValue = keeper.GetUserValue(field)?.Value;
            if (keeperValue is null)
            {
                keeperValue = values[0].Value;
                keeper.EditValue(field, keeperValue, now);
                copied.Add(field);
            }

            var alternatives = values.Where(value => value.Value != keeperValue).DistinctBy(value => value.Value).ToList();
            if (alternatives.Count > 0)
            {
                conflicts.Add(new MetadataMergeConflict(field, keeperValue, alternatives));
            }
        }

        return copied;
    }

    private static IEnumerable<TagName> UserTags(ItemMetadata metadata) =>
        metadata.Tags.Where(tag => tag.Source == MetadataSource.User).Select(tag => tag.Tag).Distinct();
}

/// <summary>
/// What <see cref="MetadataMerge"/> changed on the keeper.
/// </summary>
/// <param name="AddedTags">The user tags the keeper got from members.</param>
/// <param name="CopiedFields">The fields the keeper had no user value for and took from a member.</param>
/// <param name="Conflicts">The fields where members have other user values than the keeper now has.</param>
public sealed record MetadataMergeOutcome(
    IReadOnlyList<TagName> AddedTags,
    IReadOnlyList<DescriptiveField> CopiedFields,
    IReadOnlyList<MetadataMergeConflict> Conflicts)
{
    /// <summary>
    /// Gets a value indicating whether the keeper's metadata changed.
    /// </summary>
    public bool Changed => AddedTags.Count > 0 || CopiedFields.Count > 0 || ChosenFields.Count > 0;

    /// <summary>
    /// Gets the conflicting fields where the keeper took the value the user picked instead of keeping its own.
    /// </summary>
    public IReadOnlyList<DescriptiveField> ChosenFields { get; init; } = [];
}

/// <summary>
/// A field where the members of a duplicate group have different user values (E12-S04-T02): the keeper's value is kept,
/// and the user can pick another.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="KeeperValue">The keeper's value after the merge.</param>
/// <param name="Alternatives">The other values, each once, with the first member that has it.</param>
public sealed record MetadataMergeConflict(DescriptiveField Field, FieldValue KeeperValue, IReadOnlyList<MemberFieldValue> Alternatives);

/// <summary>
/// A member's user value of a field.
/// </summary>
/// <param name="ItemId">The member.</param>
/// <param name="Value">Its value.</param>
public sealed record MemberFieldValue(Id<MediaItem> ItemId, FieldValue Value);
