namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Transcribes speech in audio.
/// </summary>
public interface ITranscriptionProvider : IAiProvider
{
    /// <summary>
    /// Transcribes one audio file.
    /// </summary>
    /// <param name="audio">The audio.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transcript, possibly empty.</returns>
    /// <exception cref="AiProviderException">The call failed.</exception>
    Task<AiOutput<Transcript>> TranscribeAsync(AiAudio audio, CancellationToken cancellationToken);
}
