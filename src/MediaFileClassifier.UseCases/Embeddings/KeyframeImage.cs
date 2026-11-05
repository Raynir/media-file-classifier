using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;

namespace MediaFileClassifier.UseCases.Embeddings;

/// <summary>
/// A stored keyframe and its pixels.
/// </summary>
/// <param name="Keyframe">The keyframe row.</param>
/// <param name="Image">Its picture.</param>
public sealed record KeyframeImage(Keyframe Keyframe, AiImage Image);
