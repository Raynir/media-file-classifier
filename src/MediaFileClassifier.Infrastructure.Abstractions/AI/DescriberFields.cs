namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// The fields of an <see cref="ImageDescription"/> a describer can produce (E06-S04-T03). The pipeline stores only the
/// fields its describer declares, so a field the model can't produce stays empty instead of being invented, and the
/// profile and the editor can show it as not available with the current model.
/// </summary>
[Flags]
public enum DescriberFields
{
    /// <summary>
    /// No fields.
    /// </summary>
    None = 0,

    /// <summary>
    /// A short title.
    /// </summary>
    Title = 1 << 0,

    /// <summary>
    /// A description of a few sentences.
    /// </summary>
    Description = 1 << 1,

    /// <summary>
    /// A one-sentence summary.
    /// </summary>
    Summary = 1 << 2,

    /// <summary>
    /// Keywords for search.
    /// </summary>
    Tags = 1 << 3,

    /// <summary>
    /// Recognized objects.
    /// </summary>
    Objects = 1 << 4,

    /// <summary>
    /// Scenes or settings.
    /// </summary>
    Scenes = 1 << 5,

    /// <summary>
    /// What is happening.
    /// </summary>
    Actions = 1 << 6,

    /// <summary>
    /// Mood or emotions shown.
    /// </summary>
    Emotions = 1 << 7,

    /// <summary>
    /// Every field, e.g. a vision-language model with the structured output contract.
    /// </summary>
    All = Title | Description | Summary | Tags | Objects | Scenes | Actions | Emotions,
}
