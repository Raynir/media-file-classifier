namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Transcript of audio.
/// </summary>
/// <param name="Text">The full text. Empty if nothing was said.</param>
/// <param name="Language">Detected or requested language as an ISO 639-1 code, if known.</param>
/// <param name="Segments">Timed segments, in order. Empty if the provider doesn't time its output.</param>
public sealed record Transcript(string Text, string? Language, IReadOnlyList<TranscriptSegment> Segments);
