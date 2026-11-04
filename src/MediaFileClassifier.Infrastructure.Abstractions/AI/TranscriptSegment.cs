namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// One timed part of a transcript.
/// </summary>
/// <param name="Start">Start, from the beginning of the audio.</param>
/// <param name="End">End, from the beginning of the audio.</param>
/// <param name="Text">What was said.</param>
public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);
