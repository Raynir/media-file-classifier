using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// A descriptive value read from the file, such as an embedded title. One per item and field.
/// </summary>
public sealed class TechnicalFieldValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TechnicalFieldValue"/> class.
    /// </summary>
    /// <param name="itemId">Item the value describes.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    /// <param name="readAt">When the value was read from the file.</param>
    public TechnicalFieldValue(Id<MediaItem> itemId, DescriptiveField field, FieldValue value, DateTimeOffset readAt)
    {
        DescriptiveFields.EnsureValid(field, value, MetadataSource.Technical);

        ItemId = itemId;
        Field = field;
        Value = value;
        ReadAt = readAt;
    }

    public Id<MediaItem> ItemId { get; }

    public DescriptiveField Field { get; }

    public FieldValue Value { get; }

    public DateTimeOffset ReadAt { get; }
}
