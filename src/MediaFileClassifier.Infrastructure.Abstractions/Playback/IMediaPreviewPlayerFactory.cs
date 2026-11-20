namespace MediaFileClassifier.Infrastructure.Abstractions.Playback;

/// <summary>
/// Creates the preview's video players (E14-S06, D070). When the playback library is missing, the
/// <c>KnownCapabilities.VideoPlayback</c> flag is off and video opens in the OS default app instead (D026).
/// </summary>
public interface IMediaPreviewPlayerFactory
{
    /// <summary>
    /// Creates a player that decodes frames into <paramref name="sink"/>, scaled down to fit
    /// <paramref name="maxWidth"/> × <paramref name="maxHeight"/>.
    /// </summary>
    /// <param name="sink">Receives the frames.</param>
    /// <param name="maxWidth">Largest frame width, in pixels.</param>
    /// <param name="maxHeight">Largest frame height, in pixels.</param>
    /// <returns>The player; dispose of it when the preview goes.</returns>
    IMediaPreviewPlayer Create(IVideoFrameSink sink, int maxWidth, int maxHeight);
}
