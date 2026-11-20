namespace MediaFileClassifier.Infrastructure.Abstractions.Playback;

/// <summary>
/// Receives the frames an <see cref="IMediaPreviewPlayer"/> decodes into memory (the "frames route", D070). The UI
/// implements it and draws the newest frame as an ordinary control, so the preview has no airspace limits.
/// </summary>
public interface IVideoFrameSink
{
    /// <summary>
    /// Takes a decoded frame. It's called on the decoder's thread and must return quickly: a sink copies the frame
    /// and drops older ones it hasn't drawn yet, never queues them.
    /// </summary>
    /// <param name="frame">The frame, valid only during the call.</param>
    void OnFrame(VideoFrame frame);

    /// <summary>
    /// Forgets the last frame, e.g. when another file opens. Called on any thread.
    /// </summary>
    void Clear();
}
