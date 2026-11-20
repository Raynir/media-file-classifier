using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.UseCases.Metadata;

/// <summary>
/// The metadata of one item as the editor, the details panel and review read it (E07-S01-T01): for each field the
/// effective value and every layer's value with its provenance, so the UI can show "AI suggested X, you set Y".
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="Fields">Every descriptive field, in <see cref="DescriptiveFields.All"/> order, including empty ones.</param>
/// <param name="Tags">The effective tags, each with its new AI suggestion, if any.</param>
/// <param name="RejectedTags">The tag tombstones.</param>
/// <param name="HasNewAiSuggestions">Whether a field or tag has a new AI suggestion (D020).</param>
public sealed record ItemMetadataView(
    Id<MediaItem> ItemId,
    IReadOnlyList<FieldMetadataView> Fields,
    IReadOnlyList<TagMetadataView> Tags,
    IReadOnlyList<TagRejection> RejectedTags,
    bool HasNewAiSuggestions)
{
    /// <summary>
    /// Gets the names of the models that gave the AI values, tags and suggestions, by registration, for provenance
    /// tooltips (E07-S11). A model that isn't listed has no name to show.
    /// </summary>
    public IReadOnlyDictionary<Id<ModelRegistration>, string> ModelNames { get; init; } = new Dictionary<Id<ModelRegistration>, string>();

    /// <summary>
    /// Gets one field's layers.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The field's layers.</returns>
    public FieldMetadataView this[DescriptiveField field] => Fields[(int)field];

    /// <summary>
    /// Builds the view of an item's metadata.
    /// </summary>
    /// <param name="metadata">The metadata.</param>
    /// <returns>The view.</returns>
    public static ItemMetadataView From(ItemMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new ItemMetadataView(
            metadata.ItemId,
            [.. DescriptiveFields.All.Select(field => new FieldMetadataView(
                field,
                metadata.GetEffectiveValue(field),
                metadata.GetUserValue(field),
                metadata.GetLatestAiValue(field),
                metadata.GetTechnicalValue(field),
                metadata.GetSuggestedValue(field)))],
            [.. metadata.GetEffectiveTags().Select(tag => new TagMetadataView(tag, metadata.GetTagSuggestion(tag.Tag)?.Kind))],
            [.. metadata.Rejections.OrderBy(rejection => rejection.RejectedAt)],
            metadata.HasNewAiSuggestions);
    }
}

/// <summary>
/// One field of <see cref="ItemMetadataView"/>: its effective value and the value of each layer.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="Effective">The effective value with its provenance (User, then AI, then Technical), or <see langword="null"/>.</param>
/// <param name="User">The user value, edited or accepted, or <see langword="null"/>.</param>
/// <param name="LatestAi">The AI layer's value, even when a user value is effective, or <see langword="null"/>.</param>
/// <param name="Technical">The value read from the file, or <see langword="null"/>.</param>
/// <param name="Suggestion">The new AI suggestion flagged on the user value (D020), or <see langword="null"/>.</param>
public sealed record FieldMetadataView(
    DescriptiveField Field,
    ResolvedField? Effective,
    UserFieldValue? User,
    AiFieldValue? LatestAi,
    TechnicalFieldValue? Technical,
    AiFieldValue? Suggestion)
{
    /// <summary>
    /// Gets a value indicating whether the field has a new AI suggestion (E06-S07-T04).
    /// </summary>
    public bool HasNewAiSuggestion => Suggestion is not null;
}

/// <summary>
/// One effective tag of <see cref="ItemMetadataView"/>.
/// </summary>
/// <param name="Tag">The tag with its source, provenance and other-language form.</param>
/// <param name="Suggestion">The tag's new AI suggestion: <see cref="TagSuggestionKind.Added"/> on an AI tag the user
/// doesn't have, <see cref="TagSuggestionKind.Removed"/> on an accepted tag the AI no longer gives; or <see langword="null"/>.</param>
public sealed record TagMetadataView(ResolvedTag Tag, TagSuggestionKind? Suggestion);
