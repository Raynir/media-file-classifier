namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// What a metadata merge changed on the keeper (E12-S04), kept in the resolution log so an undo can revert exactly that
/// and leave later edits alone.
/// </summary>
/// <param name="AddedTags">User tags the merge added.</param>
/// <param name="UnrejectedTags">Added tags the keeper had rejected before; undo rejects them again.</param>
/// <param name="Fields">User field values the merge set.</param>
/// <param name="FavoriteAdded">Whether the merge marked the keeper favorite.</param>
public sealed record MetadataMergeRecord(
    IReadOnlyList<TagName> AddedTags,
    IReadOnlyList<TagName> UnrejectedTags,
    IReadOnlyList<MergedFieldChange> Fields,
    bool FavoriteAdded)
{
    /// <summary>Gets a value indicating whether the merge changed anything.</summary>
    public bool IsEmpty => AddedTags.Count == 0 && Fields.Count == 0 && !FavoriteAdded;

    /// <summary>
    /// Builds the record from the keeper's user layer before the merge and the metadata after it.
    /// </summary>
    /// <param name="before">The keeper's user layer before the merge.</param>
    /// <param name="after">The keeper's metadata after the merge.</param>
    /// <param name="outcome">What the merge reported.</param>
    /// <param name="favoriteAdded">Whether the merge marked the keeper favorite.</param>
    /// <returns>The record.</returns>
    public static MetadataMergeRecord From(UserLayerSnapshot before, ItemMetadata after, MetadataMergeOutcome outcome, bool favoriteAdded)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(outcome);

        var rejected = before.Rejections.Select(rejection => rejection.Tag).ToHashSet();
        List<MergedFieldChange> fields = [];
        foreach (var field in outcome.CopiedFields.Concat(outcome.ChosenFields).Distinct())
        {
            if (after.GetUserValue(field)?.Value is { } value)
            {
                fields.Add(new MergedFieldChange(field, before.UserValues.FirstOrDefault(old => old.Field == field)?.Value, value));
            }
        }

        return new MetadataMergeRecord([.. outcome.AddedTags], [.. outcome.AddedTags.Where(rejected.Contains)], fields, favoriteAdded);
    }
}

/// <summary>
/// One user field value a merge set on the keeper.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="Before">The keeper's user value before; none if it had none.</param>
/// <param name="After">The value the merge set.</param>
public sealed record MergedFieldChange(DescriptiveField Field, FieldValue? Before, FieldValue After);

/// <summary>
/// What reverting a merge changed.
/// </summary>
/// <param name="RemovedTags">Tags it removed.</param>
/// <param name="RevertedFields">Fields it set back.</param>
/// <param name="KeptFields">Fields left as they were because the user changed them after the merge.</param>
public sealed record MetadataMergeRevertOutcome(IReadOnlyList<TagName> RemovedTags, IReadOnlyList<DescriptiveField> RevertedFields, IReadOnlyList<DescriptiveField> KeptFields)
{
    /// <summary>Gets a value indicating whether anything was reverted.</summary>
    public bool Changed => RemovedTags.Count > 0 || RevertedFields.Count > 0;
}

/// <summary>
/// Reverts a metadata merge on the keeper: only what the merge did, and only where it still holds the merged value, so
/// edits the user made after the merge stay (E12-S04 undo).
/// </summary>
public static class MetadataMergeRevert
{
    /// <summary>
    /// Reverts the merge.
    /// </summary>
    /// <param name="keeper">The keeper's current metadata.</param>
    /// <param name="record">What the merge changed.</param>
    /// <param name="now">Current time.</param>
    /// <returns>What was reverted.</returns>
    public static MetadataMergeRevertOutcome Apply(ItemMetadata keeper, MetadataMergeRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(keeper);
        ArgumentNullException.ThrowIfNull(record);

        List<TagName> removed = [];
        foreach (var tag in record.AddedTags)
        {
            if (record.UnrejectedTags.Contains(tag))
            {
                if (keeper.Tags.Any(assignment => assignment.Source == MetadataSource.User && assignment.Tag == tag))
                {
                    keeper.RejectTag(tag, now);
                    removed.Add(tag);
                }
            }
            else if (keeper.RemoveTag(tag))
            {
                removed.Add(tag);
            }
        }

        List<DescriptiveField> reverted = [], kept = [];
        foreach (var change in record.Fields)
        {
            if (keeper.GetUserValue(change.Field)?.Value != change.After)
            {
                kept.Add(change.Field);
                continue;
            }

            if (change.Before is { } before)
            {
                keeper.EditValue(change.Field, before, now);
            }
            else
            {
                keeper.ClearUserValue(change.Field);
            }

            reverted.Add(change.Field);
        }

        return new MetadataMergeRevertOutcome(removed, reverted, kept);
    }
}
