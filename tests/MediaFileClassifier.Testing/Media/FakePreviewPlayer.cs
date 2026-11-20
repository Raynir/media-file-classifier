using MediaFileClassifier.Infrastructure.Abstractions.Playback;

namespace MediaFileClassifier.Testing.Media;

/// <summary>
/// A preview player that records the requests and whose state the test sets (E14-S06). The test raises the events
/// with <see cref="SetState"/> and <see cref="SetPosition"/>, as LibVLC would from its threads.
/// </summary>
public sealed class FakePreviewPlayer : IMediaPreviewPlayer
{
    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public event EventHandler? PositionChanged;

    /// <summary>
    /// Gets the requests, e.g. <c>Open path 0:00:00 paused</c>, <c>Play</c>, <c>Seek 0:00:05</c>.
    /// </summary>
    public List<string> Requests { get; } = [];

    /// <summary>
    /// Gets the frame sink the player was created with.
    /// </summary>
    public IVideoFrameSink? Sink { get; init; }

    /// <summary>
    /// Gets a value indicating whether the player is disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public PlaybackState State { get; private set; }

    /// <inheritdoc />
    public TimeSpan Position { get; private set; }

    /// <inheritdoc />
    public TimeSpan Duration { get; private set; }

    /// <inheritdoc />
    public string? Error { get; private set; }

    /// <inheritdoc />
    public int Volume { get; set; } = 100;

    /// <inheritdoc />
    public bool IsMuted { get; set; }

    /// <summary>
    /// Sets the state, the length and the error, and raises <see cref="StateChanged"/>.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="duration">The length, or <see langword="null"/> to keep it.</param>
    /// <param name="error">The error, for <see cref="PlaybackState.Failed"/>.</param>
    public void SetState(PlaybackState state, TimeSpan? duration = null, string? error = null)
    {
        State = state;
        Duration = duration ?? Duration;
        Error = error;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the position and raises <see cref="PositionChanged"/>.
    /// </summary>
    /// <param name="position">The position.</param>
    public void SetPosition(TimeSpan position)
    {
        Position = position;
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Open(string path, TimeSpan start, bool play)
    {
        Requests.Add($"Open {path} {start} {(play ? "playing" : "paused")}");
        State = PlaybackState.Opening;
        Position = start;
    }

    /// <inheritdoc />
    public void Play() => Requests.Add("Play");

    /// <inheritdoc />
    public void Pause() => Requests.Add("Pause");

    /// <inheritdoc />
    public void Seek(TimeSpan position)
    {
        Requests.Add($"Seek {position}");
        Position = position;
    }

    /// <inheritdoc />
    public void Close()
    {
        Requests.Add("Close");
        State = PlaybackState.Closed;
    }

    /// <inheritdoc />
    public void Dispose() => IsDisposed = true;
}

/// <summary>
/// Creates <see cref="FakePreviewPlayer"/>s and keeps them for the test.
/// </summary>
public sealed class FakePreviewPlayerFactory : IMediaPreviewPlayerFactory
{
    /// <summary>
    /// Gets the players created, in order.
    /// </summary>
    public List<FakePreviewPlayer> Players { get; } = [];

    /// <summary>
    /// Gets the last player created.
    /// </summary>
    public FakePreviewPlayer Player => Players[^1];

    /// <inheritdoc />
    public IMediaPreviewPlayer Create(IVideoFrameSink sink, int maxWidth, int maxHeight)
    {
        var player = new FakePreviewPlayer { Sink = sink };
        Players.Add(player);
        return player;
    }
}
