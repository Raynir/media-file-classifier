using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.UseCases.Tags;

/// <summary>
/// A normalised AI tag and the other-language form the model wrote for it (E06-S04-T09).
/// </summary>
/// <param name="Tag">The tag.</param>
/// <param name="OtherForm">The form, or <c>null</c> when the model wrote none.</param>
public sealed record NormalizedTag(TagName Tag, string? OtherForm);
