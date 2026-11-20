namespace MediaFileClassifier.Infrastructure.Abstractions.Playback;

/// <summary>
/// Plays a video in the preview (E14-S06, D070). One player is reused for the items shown one after another. The
/// methods only queue the request and return at once, so the UI thread never waits for the decoder; requests run in
/// the order they were made. Properties are snapshots that are safe to read on any thread.
/// </summary>
/// <remarks>
/// Events are raised on a background thread. Handlers must not call back into the player synchronously and should
/// post to the UI thread.
/// </remarks>
public interface IMediaPreviewPlayer : IDisposable
{
    /// <summary>
    /// Raised when <see cref="State"/>, <see cref="Duration"/> or <see cref="Error"/> changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Raised when <see cref="Position"/> changes during playback, at most a few times a second.
    /// </summary>
    event EventHandler? PositionChanged;

    /// <summary>
    /// Gets the playback state.
    /// </summary>
    PlaybackState State { get; }

    /// <summary>
    /// Gets the playback position.
    /// </summary>
    TimeSpan Position { get; }

    /// <summary>
    /// Gets the length of the open file, or <see cref="TimeSpan.Zero"/> while it isn't known.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Gets a technical description of the last failure, for the log, or <see langword="null"/>.
    /// </summary>
    string? Error { get; }

    /// <summary>
    /// Gets or sets the volume, from 0 to 100.
    /// </summary>
    int Volume { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sound is muted.
    /// </summary>
    bool IsMuted { get; set; }

    /// <summary>
    /// Opens a file, closing the one before. The player shows the frame at <paramref name="start"/> and pauses
    /// there, or starts playing when <paramref name="play"/> is set ("jump to the moment").
    /// </summary>
    /// <param name="path">The full path of a local file.</param>
    /// <param name="start">Where to start.</param>
    /// <param name="play">Whether to start playing, rather than pause on the first frame.</param>
    void Open(string path, TimeSpan start, bool play);

    /// <summary>
    /// Plays from the current position; at the end, plays from the start again.
    /// </summary>
    void Play();

    /// <summary>
    /// Pauses.
    /// </summary>
    void Pause();

    /// <summary>
    /// Moves to a position, keeping whether it plays. LibVLC lands up to a few frames before it (D070).
    /// </summary>
    /// <param name="position">The position, clamped to the file's length.</param>
    void Seek(TimeSpan position);

    /// <summary>
    /// Stops playback and closes the file.
    /// </summary>
    void Close();
}
