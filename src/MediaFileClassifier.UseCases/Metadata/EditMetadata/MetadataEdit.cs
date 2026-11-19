using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.UseCases.Metadata.EditMetadata;

/// <summary>
/// One change to an item's metadata (E07-S01-T02). Every edit is applied through the <see cref="ItemMetadata"/> domain
/// methods, so the precedence rules hold (D020, D047). An edit that doesn't apply to an item, e.g. confirming a field
/// that has no AI value, leaves that item as it is, so one list of edits can be applied to a whole selection.
/// </summary>
public abstract record MetadataEdit
{
    private protected MetadataEdit()
    {
    }

    /// <summary>
    /// Gets the field the edit changes, or <see langword="null"/> for a tag edit.
    /// </summary>
    internal virtual DescriptiveField? ChangedField => null;

    /// <summary>
    /// Applies the edit to one item's metadata.
    /// </summary>
    /// <param name="metadata">The item's metadata.</param>
    /// <param name="now">Current time.</param>
    /// <returns>Whether the edit changed the item.</returns>
    internal abstract bool ApplyTo(ItemMetadata metadata, DateTimeOffset now);

    /// <summary>
    /// Sets a user value, with origin Edited (D020). It replaces any earlier user value and clears its suggestion.
    /// </summary>
    /// <param name="Field">The field.</param>
    /// <param name="Value">The value as the user wrote it: a text for a text field, a list for a list field.</param>
    public sealed record SetField(DescriptiveField Field, FieldValue Value) : MetadataEdit
    {
        internal override DescriptiveField? ChangedField => Field;

        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now)
        {
            if (metadata.GetUserValue(Field) is { Origin: UserValueOrigin.Edited, HasNewAiSuggestion: false } user && user.Value == Value)
            {
                return false;
            }

            metadata.EditValue(Field, Value, now);
            return true;
        }
    }

    /// <summary>
    /// Removes the user value of a field, so the AI value (or the file's) becomes effective again.
    /// </summary>
    /// <param name="Field">The field.</param>
    public sealed record RevertField(DescriptiveField Field) : MetadataEdit
    {
        internal override DescriptiveField? ChangedField => Field;

        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now)
        {
            if (metadata.GetUserValue(Field) is null)
            {
                return false;
            }

            metadata.ClearUserValue(Field);
            return true;
        }
    }

    /// <summary>
    /// Confirms the effective AI value of a field: it is copied into the user layer with origin Accepted and a reference
    /// to its run (D020), together with its other-language form (D047). A field with a user value is left as it is.
    /// </summary>
    /// <param name="Field">The field.</param>
    public sealed record ConfirmField(DescriptiveField Field) : MetadataEdit
    {
        internal override DescriptiveField? ChangedField => Field;

        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now)
        {
            if (metadata.GetUserValue(Field) is not null || metadata.GetLatestAiValue(Field) is null)
            {
                return false;
            }

            metadata.AcceptAiValue(Field, now);
            return true;
        }
    }

    /// <summary>
    /// Replaces a field's user value with its new AI suggestion (origin Accepted, referencing the suggesting run) and
    /// clears the flag.
    /// </summary>
    /// <param name="Field">The field.</param>
    public sealed record AcceptFieldSuggestion(DescriptiveField Field) : MetadataEdit
    {
        internal override DescriptiveField? ChangedField => Field;

        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now)
        {
            if (metadata.GetSuggestedValue(Field) is null)
            {
                return false;
            }

            metadata.AcceptSuggestion(Field, now);
            return true;
        }
    }

    /// <summary>
    /// Keeps a field's user value and clears its new AI suggestion, until a run produces a different value.
    /// </summary>
    /// <param name="Field">The field.</param>
    public sealed record DismissFieldSuggestion(DescriptiveField Field) : MetadataEdit
    {
        internal override DescriptiveField? ChangedField => Field;

        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now)
        {
            if (metadata.GetUserValue(Field) is not { HasNewAiSuggestion: true })
            {
                return false;
            }

            metadata.DismissSuggestion(Field);
            return true;
        }
    }

    /// <summary>
    /// Adds a tag the user typed. The command cleans the text (<see cref="Tags.TagNormalizer.CleanUserTag"/>) and checks
    /// it, then stores it in the case it was typed (D047) and removes its tombstone. A tag the user already has (tags
    /// compare ignoring case) is left as it is, with its case.
    /// </summary>
    /// <param name="Text">The tag as typed.</param>
    public sealed record AddTag(string Text) : MetadataEdit
    {
        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now)
        {
            var tag = TagName.From(Text);
            if (metadata.Tags.Any(assigned => assigned.Source == MetadataSource.User && assigned.Tag == tag) && !metadata.Rejections.Any(rejection => rejection.Tag == tag))
            {
                return false;
            }

            metadata.AddTag(tag, now);
            return true;
        }
    }

    /// <summary>
    /// Removes a user tag without a tombstone. If an AI run or the file gives it too, that one shows again.
    /// </summary>
    /// <param name="Tag">The tag.</param>
    public sealed record RemoveTag(TagName Tag) : MetadataEdit
    {
        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now) => metadata.RemoveTag(Tag);
    }

    /// <summary>
    /// Rejects a tag: the user tag is removed and a tombstone keeps AI runs and the file from adding it again, with both
    /// language forms (D047).
    /// </summary>
    /// <param name="Tag">The tag.</param>
    public sealed record RejectTag(TagName Tag) : MetadataEdit
    {
        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now)
        {
            if (metadata.Rejections.Any(rejection => rejection.Tag == Tag) && !metadata.GetEffectiveTags().Any(tag => tag.Tag == Tag))
            {
                return false;
            }

            metadata.RejectTag(Tag, now);
            return true;
        }
    }

    /// <summary>
    /// Confirms an effective AI tag: it is copied into the user layer with origin Accepted, with both language forms (D047).
    /// </summary>
    /// <param name="Tag">The tag.</param>
    public sealed record ConfirmTag(TagName Tag) : MetadataEdit
    {
        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now)
        {
            if (!metadata.GetEffectiveTags().Any(tag => tag.Tag == Tag && tag.Source == MetadataSource.AI))
            {
                return false;
            }

            metadata.AcceptTag(Tag, now);
            return true;
        }
    }

    /// <summary>
    /// Follows a tag's new AI suggestion: an added tag is confirmed, an accepted tag the AI dropped is removed.
    /// </summary>
    /// <param name="Tag">The tag.</param>
    public sealed record AcceptTagSuggestion(TagName Tag) : MetadataEdit
    {
        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now)
        {
            if (metadata.GetTagSuggestion(Tag) is null)
            {
                return false;
            }

            metadata.AcceptTagSuggestion(Tag, now);
            return true;
        }
    }

    /// <summary>
    /// Keeps a tag as it is and clears its new AI suggestion, until a run changes it again.
    /// </summary>
    /// <param name="Tag">The tag.</param>
    public sealed record DismissTagSuggestion(TagName Tag) : MetadataEdit
    {
        internal override bool ApplyTo(ItemMetadata metadata, DateTimeOffset now)
        {
            if (metadata.GetTagSuggestion(Tag) is null)
            {
                return false;
            }

            metadata.DismissTagSuggestion(Tag);
            return true;
        }
    }
}
