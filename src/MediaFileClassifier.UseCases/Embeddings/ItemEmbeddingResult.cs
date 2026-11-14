using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.UseCases.Embeddings;

/// <summary>
/// What <see cref="ItemEmbeddingAnalyzer"/> stored.
/// </summary>
/// <param name="Model">The model the vectors belong to, or <c>null</c> if nothing was stored.</param>
/// <param name="KeyframeCount">Per-keyframe vectors stored next to the item vector.</param>
/// <param name="Warnings">Frame sampling warnings, to store like thumbnail warnings.</param>
public sealed record ItemEmbeddingResult(ModelRegistration? Model, int KeyframeCount, IReadOnlyList<ExtractionWarning> Warnings);
