namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Structured description of an image (spec 1.4). LLM describers produce it as JSON matching the analysis result
/// schema (E01-S05-T05).
/// </summary>
/// <param name="Title">A short title.</param>
/// <param name="Description">A description of a few sentences.</param>
/// <param name="Summary">A one-sentence summary.</param>
/// <param name="Tags">Keywords for search.</param>
/// <param name="Objects">Recognized objects.</param>
/// <param name="Scenes">Scenes or settings, e.g. <c>beach</c> or <c>kitchen</c>.</param>
/// <param name="Actions">What is happening.</param>
/// <param name="Emotions">Mood or emotions shown.</param>
public sealed record ImageDescription(
    string Title,
    string Description,
    string Summary,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Objects,
    IReadOnlyList<string> Scenes,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Emotions);
