namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Audio to transcribe, usually a track extracted from a video by the pipeline.
/// </summary>
/// <param name="FilePath">Full path of the audio file.</param>
/// <param name="MediaType">MIME type, e.g. <c>audio/wav</c>.</param>
/// <param name="LanguageHint">Expected language as an ISO 639-1 code, or <c>null</c> to detect it.</param>
public sealed record AiAudio(string FilePath, string MediaType, string? LanguageHint = null);
