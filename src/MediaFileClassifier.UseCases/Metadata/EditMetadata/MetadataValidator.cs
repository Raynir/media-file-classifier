using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.UseCases.Tags;

namespace MediaFileClassifier.UseCases.Metadata.EditMetadata;

/// <summary>
/// Checks the edits of a command before anything is written (E07-S01-T04), and cleans the tags the user typed.
/// </summary>
public static class MetadataValidator
{
    /// <summary>
    /// Checks every edit and returns the edits to apply, with typed tags cleaned (D047). It stops at the first problem.
    /// </summary>
    /// <param name="edits">The edits.</param>
    /// <returns>The edits to apply, or the first error.</returns>
    public static (IReadOnlyList<MetadataEdit> Edits, MetadataValidationError? Error) Validate(IReadOnlyList<MetadataEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var cleaned = new List<MetadataEdit>(edits.Count);
        foreach (var edit in edits)
        {
            MetadataValidationError? error = null;
            var result = edit;
            switch (edit)
            {
                case MetadataEdit.SetField set:
                    error = ValidateField(set.Field, set.Value);
                    break;
                case MetadataEdit.AddTag add:
                    var (tag, tagError) = CleanTag(add.Text);
                    error = tagError;
                    result = add with { Text = tag };
                    break;
            }

            if (error is not null)
            {
                return ([], error);
            }

            cleaned.Add(result);
        }

        return (cleaned, null);
    }

    private static MetadataValidationError? ValidateField(DescriptiveField field, FieldValue value)
    {
        if (value.IsList != DescriptiveFields.IsList(field))
        {
            return new MetadataValidationError(MetadataValidationCode.WrongValueKind, Field: field);
        }

        if (!value.IsList)
        {
            var limit = MetadataLimits.TextLength(field);
            return value.Text.Length > limit ? new MetadataValidationError(MetadataValidationCode.TextTooLong, Field: field, Limit: limit) : null;
        }

        if (value.Items.Count > MetadataLimits.ListItems)
        {
            return new MetadataValidationError(MetadataValidationCode.TooManyListItems, Field: field, Limit: MetadataLimits.ListItems);
        }

        return value.Items.Any(item => item.Length > MetadataLimits.ListItemLength)
            ? new MetadataValidationError(MetadataValidationCode.ListItemTooLong, Field: field, Limit: MetadataLimits.ListItemLength)
            : null;
    }

    private static (string Tag, MetadataValidationError? Error) CleanTag(string? text)
    {
        var tag = TagNormalizer.CleanUserTag(text);
        if (tag.Length == 0)
        {
            return (tag, new MetadataValidationError(MetadataValidationCode.TagEmpty, Tag: text));
        }

        if (tag.Any(character => char.IsControl(character) || character is ',' or ';'))
        {
            return (tag, new MetadataValidationError(MetadataValidationCode.TagInvalidCharacter, Tag: text));
        }

        return tag.Length > MetadataLimits.TagLength
            ? (tag, new MetadataValidationError(MetadataValidationCode.TagTooLong, Tag: text, Limit: MetadataLimits.TagLength))
            : (tag, null);
    }
}
