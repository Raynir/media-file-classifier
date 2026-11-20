namespace MediaFileClassifier.Infrastructure.Abstractions.Playback;

/// <summary>
/// The state of an <see cref="IMediaPreviewPlayer"/> (E14-S06).
/// </summary>
public enum PlaybackState
{
    /// <summary>Nothing is open.</summary>
    Closed,

    /// <summary>A file is opening; no frame has been shown yet.</summary>
    Opening,

    /// <summary>A frame is shown and playback is paused.</summary>
    Paused,

    /// <summary>The file is playing.</summary>
    Playing,

    /// <summary>Playback reached the end of the file.</summary>
    Ended,

    /// <summary>The file couldn't be opened or decoded; the preview falls back to the OS default app (D026).</summary>
    Failed,
}
