using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.UseCases.Metadata.EditMetadata;

/// <summary>
/// Why an edit was refused.
/// </summary>
public enum MetadataValidationCode
{
    /// <summary>
    /// A text was given for a list field, or a list for a text field.
    /// </summary>
    WrongValueKind,

    /// <summary>
    /// A text is longer than <see cref="MetadataValidationError.Limit"/> characters.
    /// </summary>
    TextTooLong,

    /// <summary>
    /// A list has more than <see cref="MetadataValidationError.Limit"/> entries.
    /// </summary>
    TooManyListItems,

    /// <summary>
    /// A list entry is longer than <see cref="MetadataValidationError.Limit"/> characters.
    /// </summary>
    ListItemTooLong,

    /// <summary>
    /// A tag is empty after cleaning.
    /// </summary>
    TagEmpty,

    /// <summary>
    /// A tag is longer than <see cref="MetadataValidationError.Limit"/> characters.
    /// </summary>
    TagTooLong,

    /// <summary>
    /// A tag holds a control character, a comma or a semicolon.
    /// </summary>
    TagInvalidCharacter,

    /// <summary>
    /// An item would get more than <see cref="MetadataValidationError.Limit"/> user tags.
    /// </summary>
    TooManyTags,
}

/// <summary>
/// The first problem found in an <see cref="EditMetadataCommand"/>. Nothing is written when there is one; the UI turns
/// the code into a message.
/// </summary>
/// <param name="Code">What is wrong.</param>
/// <param name="Field">The field, for a field edit.</param>
/// <param name="Tag">The tag as typed, for a tag edit.</param>
/// <param name="Limit">The limit that was passed, where the code has one.</param>
/// <param name="ItemId">The item that would get too many tags, for <see cref="MetadataValidationCode.TooManyTags"/>.</param>
public sealed record MetadataValidationError(
    MetadataValidationCode Code,
    DescriptiveField? Field = null,
    string? Tag = null,
    int? Limit = null,
    Id<MediaItem>? ItemId = null);
