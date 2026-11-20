using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using MediaFileClassifier.Infrastructure.Abstractions.Playback;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace MediaFileClassifier.Infrastructure.Playback;

/// <summary>
/// The preview's video player on LibVLC's frames route (D070): LibVLC decodes and plays the sound, and copies each
/// video frame, scaled to the preview size, into memory for an <see cref="IVideoFrameSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// Threads: every LibVLC call runs on one command thread, in the order the requests came, so the UI thread never
/// waits for LibVLC (stopping can take a while) and LibVLC is never called from its own event threads, which
/// deadlocks. LibVLC's events only update the snapshot under <see cref="gate"/>. Frames arrive on LibVLC's video
/// thread.
/// </para>
/// <para>
/// The reported state follows the requests (play or pause) once the first frame of the file is shown, so pausing
/// on the first frame doesn't flicker through <see cref="PlaybackState.Playing"/>. A file that shows no frame within
/// <see cref="FirstFrameTimeout"/>, e.g. an unsupported codec or no video track, fails (D026).
/// </para>
/// </remarks>
[SuppressMessage(
    "Usage",
    "CA2213:Disposable fields should be disposed",
    Justification = "The command thread disposes of the LibVLC player and the queue when Dispose completes the queue.")]
public sealed partial class LibVlcPreviewPlayer : IMediaPreviewPlayer
{
    /// <summary>
    /// How long a file may take to show its first frame before it counts as failed.
    /// </summary>
    internal static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The shortest time between two <see cref="PositionChanged"/> events during playback.
    /// </summary>
    internal static readonly TimeSpan PositionInterval = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan PausedStartTime = TimeSpan.FromMilliseconds(1);

    private static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);

    private readonly LibVLC libVlc;
    private readonly MediaPlayer player;
    private readonly IVideoFrameSink sink;
    private readonly int maxWidth;
    private readonly int maxHeight;
    private readonly ILogger logger;
    private readonly BlockingCollection<Action> commands = [];
    private readonly Thread commandThread;
    private readonly Lock gate = new();

    // LibVLC holds these delegates as native function pointers; the fields keep them alive.
    private readonly MediaPlayer.LibVLCVideoFormatCb formatCallback;
    private readonly MediaPlayer.LibVLCVideoCleanupCb cleanupCallback;
    private readonly MediaPlayer.LibVLCVideoLockCb lockCallback;
    private readonly MediaPlayer.LibVLCVideoDisplayCb displayCallback;

    // The snapshot, under gate.
    private string? path;
    private int generation;
    private bool requestedPlaying;
    private bool shownFirstFrame;
    private bool ended;
    private bool failed;
    private string? error;
    private TimeSpan duration;
    private TimeSpan position;
    private int volume = 100;
    private bool muted;
    private long lastPositionEvent;

    // A seek waiting for the command thread, in ticks, or -1. Seeks while one waits only replace its target.
    private long pendingSeek = -1;

    // The shown size of the open file's video, with square pixels, or 0 when parsing didn't find it. Written on the
    // command thread before playback starts, read on the video thread.
    private volatile int videoWidth;
    private volatile int videoHeight;

    // The frame buffer, used only on LibVLC's video thread.
    private IntPtr buffer;
    private int width;
    private int height;
    private int stride;

    private int disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibVlcPreviewPlayer"/> class.
    /// </summary>
    /// <param name="libVlc">The LibVLC instance.</param>
    /// <param name="sink">Receives the frames.</param>
    /// <param name="maxWidth">Largest frame width, in pixels.</param>
    /// <param name="maxHeight">Largest frame height, in pixels.</param>
    /// <param name="logger">Logs failures.</param>
    public LibVlcPreviewPlayer(LibVLC libVlc, IVideoFrameSink sink, int maxWidth, int maxHeight, ILogger<LibVlcPreviewPlayer> logger)
    {
        ArgumentNullException.ThrowIfNull(libVlc);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWidth, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeight, 2);

        this.libVlc = libVlc;
        this.sink = sink;
        this.maxWidth = maxWidth;
        this.maxHeight = maxHeight;
        this.logger = logger;
        player = new MediaPlayer(libVlc);
        formatCallback = OnFormat;
        cleanupCallback = OnCleanup;
        lockCallback = OnLock;
        displayCallback = OnDisplay;
        player.SetVideoFormatCallbacks(formatCallback, cleanupCallback);
        player.SetVideoCallbacks(lockCallback, null, displayCallback);
        player.Playing += OnPlaying;
        player.EndReached += OnEndReached;
        player.EncounteredError += OnEncounteredError;
        player.LengthChanged += OnLengthChanged;
        player.TimeChanged += OnTimeChanged;
        commandThread = new Thread(RunCommands) { IsBackground = true, Name = "LibVLC preview" };
        commandThread.Start();
    }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public event EventHandler? PositionChanged;

    /// <inheritdoc />
    public PlaybackState State
    {
        get
        {
            lock (gate)
            {
                return CurrentState();
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan Position
    {
        get
        {
            lock (gate)
            {
                return position;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan Duration
    {
        get
        {
            lock (gate)
            {
                return duration;
            }
        }
    }

    /// <inheritdoc />
    public string? Error
    {
        get
        {
            lock (gate)
            {
                return error;
            }
        }
    }

    /// <inheritdoc />
    public int Volume
    {
        get
        {
            lock (gate)
            {
                return volume;
            }
        }

        set
        {
            lock (gate)
            {
                volume = Math.Clamp(value, 0, 100);
            }

            Enqueue(ApplyAudio);
        }
    }

    /// <inheritdoc />
    public bool IsMuted
    {
        get
        {
            lock (gate)
            {
                return muted;
            }
        }

        set
        {
            lock (gate)
            {
                muted = value;
            }

            Enqueue(ApplyAudio);
        }
    }

    /// <inheritdoc />
    public void Open(string path, TimeSpan start, bool play)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        start = start < TimeSpan.Zero ? TimeSpan.Zero : start;
        Enqueue(() => OpenNow(path, start, play));
    }

    /// <inheritdoc />
    public void Play() => Enqueue(PlayNow);

    /// <inheritdoc />
    public void Pause() => Enqueue(PauseNow);

    /// <inheritdoc />
    public void Seek(TimeSpan position)
    {
        lock (gate)
        {
            if (path is null || failed)
            {
                return;
            }

            position = Clamp(position);
            this.position = position;
        }

        // Shown at once, so the timeline follows a drag before LibVLC gets there.
        PositionChanged?.Invoke(this, EventArgs.Empty);
        if (Interlocked.Exchange(ref pendingSeek, position.Ticks) == -1)
        {
            Enqueue(SeekNow);
        }
    }

    /// <inheritdoc />
    public void Close() => Enqueue(CloseNow);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // The command thread stops LibVLC and releases the player once the queued requests are done.
        commands.CompleteAdding();
        if (!commandThread.Join(DisposeTimeout))
        {
            LogDisposeTimedOut(logger, DisposeTimeout.TotalSeconds);
        }
    }

    private static string FormatSeconds(TimeSpan time) => time.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private TimeSpan Clamp(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return duration > TimeSpan.Zero && time > duration ? duration : time;
    }

    private PlaybackState CurrentState()
    {
        if (failed)
        {
            return PlaybackState.Failed;
        }

        if (path is null)
        {
            return PlaybackState.Closed;
        }

        if (ended)
        {
            return PlaybackState.Ended;
        }

        if (!shownFirstFrame)
        {
            return PlaybackState.Opening;
        }

        return requestedPlaying ? PlaybackState.Playing : PlaybackState.Paused;
    }

    private void Enqueue(Action command)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        try
        {
            commands.Add(command);
        }
        catch (InvalidOperationException)
        {
            // Disposed meanwhile.
        }
    }

    private void RunCommands()
    {
        foreach (var command in commands.GetConsumingEnumerable())
        {
            try
            {
                command();
            }
            catch (Exception ex) when (ex is VLCException or InvalidOperationException or IOException or ArgumentException)
            {
                LogCommandFailed(logger, ex);
                Fail(ex.Message);
            }
        }

        try
        {
            player.Stop();
        }
        finally
        {
            player.Playing -= OnPlaying;
            player.EndReached -= OnEndReached;
            player.EncounteredError -= OnEncounteredError;
            player.LengthChanged -= OnLengthChanged;
            player.TimeChanged -= OnTimeChanged;
            player.Dispose();
            commands.Dispose();
        }
    }

    private void OpenNow(string file, TimeSpan start, bool play)
    {
        Interlocked.Exchange(ref pendingSeek, -1);
        player.Stop();
        sink.Clear();
        int opened;
        lock (gate)
        {
            opened = ++generation;
            path = file;
            requestedPlaying = play;
            shownFirstFrame = false;
            ended = false;
            failed = false;
            error = null;
            duration = TimeSpan.Zero;
            position = start;
        }

        using (var media = new Media(libVlc, file, FromType.FromPath))
        {
            ReadVideoSize(media);
            if (!play)
            {
                // LibVLC pauses at the start. It shows a frame there only after a seek, so a paused start always
                // seeks, if only to the first millisecond.
                media.AddOption(":start-paused");
                media.AddOption(":start-time=" + FormatSeconds(start > PausedStartTime ? start : PausedStartTime));
            }
            else if (start > TimeSpan.Zero)
            {
                media.AddOption(":start-time=" + FormatSeconds(start));
            }

            player.Media = media;
        }

        RaiseStateChanged();
        if (!player.Play())
        {
            Fail("LibVLC couldn't start playback.");
            return;
        }

        _ = WatchFirstFrameAsync(opened);
    }

    private void ReadVideoSize(Media media)
    {
        // LibVLC's format callback gets the decoder's padded size (e.g. 320×258 for 320×240), so the shown size comes
        // from the file. Parsing a local file only reads its headers.
        videoWidth = 0;
        videoHeight = 0;
        var status = media.Parse(MediaParseOptions.ParseLocal, timeout: (int)ParseTimeout.TotalMilliseconds).GetAwaiter().GetResult();
        if (status != MediaParsedStatus.Done)
        {
            return;
        }

        foreach (var track in media.Tracks)
        {
            if (track.TrackType != TrackType.Video || track.Data.Video.Width == 0 || track.Data.Video.Height == 0)
            {
                continue;
            }

            var video = track.Data.Video;
            var aspect = video.SarNum > 0 && video.SarDen > 0 ? (double)video.SarNum / video.SarDen : 1.0;
            var shownWidth = (int)Math.Round(video.Width * aspect);
            var rotated = video.Orientation is VideoOrientation.LeftTop or VideoOrientation.RightBottom
                or VideoOrientation.LeftBottom or VideoOrientation.RightTop;
            videoWidth = rotated ? (int)video.Height : shownWidth;
            videoHeight = rotated ? shownWidth : (int)video.Height;
            return;
        }
    }

    private void PlayNow()
    {
        string? file;
        bool atEnd;
        lock (gate)
        {
            if (path is null || failed)
            {
                return;
            }

            file = path;
            atEnd = ended;
            requestedPlaying = true;
        }

        if (atEnd)
        {
            OpenNow(file, TimeSpan.Zero, play: true);
            return;
        }

        player.SetPause(false);
        RaiseStateChanged();
    }

    private void PauseNow()
    {
        lock (gate)
        {
            if (path is null || failed || ended)
            {
                return;
            }

            requestedPlaying = false;
        }

        player.SetPause(true);
        RaiseStateChanged();
    }

    private void SeekNow()
    {
        var ticks = Interlocked.Exchange(ref pendingSeek, -1);
        if (ticks < 0)
        {
            return;
        }

        var target = TimeSpan.FromTicks(ticks);
        string? file;
        bool reopen;
        bool play;
        lock (gate)
        {
            if (path is null || failed)
            {
                return;
            }

            file = path;

            // Before the first frame or after the end, LibVLC has no input to seek in: open it again at the target.
            reopen = ended || !shownFirstFrame;
            play = requestedPlaying && !ended;
        }

        if (reopen)
        {
            OpenNow(file, target, play);
            return;
        }

        player.Time = (long)target.TotalMilliseconds;
    }

    private void CloseNow()
    {
        Interlocked.Exchange(ref pendingSeek, -1);
        player.Stop();
        sink.Clear();
        lock (gate)
        {
            ++generation;
            path = null;
            requestedPlaying = false;
            shownFirstFrame = false;
            ended = false;
            failed = false;
            error = null;
            duration = TimeSpan.Zero;
            position = TimeSpan.Zero;
        }

        RaiseStateChanged();
    }

    private void ApplyAudio()
    {
        int level;
        bool mute;
        lock (gate)
        {
            level = volume;
            mute = muted;
        }

        player.Volume = level;
        player.Mute = mute;
    }

    private async Task WatchFirstFrameAsync(int opened)
    {
        await Task.Delay(FirstFrameTimeout).ConfigureAwait(false);
        lock (gate)
        {
            if (generation != opened || shownFirstFrame || failed || path is null)
            {
                return;
            }
        }

        LogNoFrame(logger, FirstFrameTimeout.TotalSeconds);
        Fail($"No video frame within {FirstFrameTimeout.TotalSeconds:0} seconds: the codec may be unsupported, or the file has no video.");

        // Stop decoding, e.g. the sound of a file whose video can't be shown, unless another file opened meanwhile.
        Enqueue(() =>
        {
            lock (gate)
            {
                if (generation != opened)
                {
                    return;
                }
            }

            player.Stop();
        });
    }

    private void Fail(string message)
    {
        lock (gate)
        {
            if (path is null)
            {
                return;
            }

            failed = true;
            error = message;
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void OnPlaying(object? sender, EventArgs e) =>

        // The audio output exists only now, so the volume and mute set before take effect here.
        Enqueue(ApplyAudio);

    private void OnEndReached(object? sender, EventArgs e)
    {
        lock (gate)
        {
            ended = true;
            requestedPlaying = false;
            if (duration > TimeSpan.Zero)
            {
                position = duration;
            }
        }

        RaiseStateChanged();
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        LogPlaybackError(logger);
        Fail("LibVLC couldn't open or decode the file.");
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        lock (gate)
        {
            duration = TimeSpan.FromMilliseconds(Math.Max(0, e.Length));
        }

        RaiseStateChanged();
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        if (Interlocked.Read(ref pendingSeek) >= 0)
        {
            // A seek is on its way; the old time would make the timeline jump back.
            return;
        }

        var now = Environment.TickCount64;
        lock (gate)
        {
            position = TimeSpan.FromMilliseconds(Math.Max(0, e.Time));
            if (now - lastPositionEvent < (long)PositionInterval.TotalMilliseconds)
            {
                return;
            }

            lastPositionEvent = now;
        }

        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private uint OnFormat(ref IntPtr opaque, IntPtr chroma, ref uint sourceWidth, ref uint sourceHeight, ref uint pitches, ref uint lines)
    {
        // LibVLC scales to the preview size, so a 4K source isn't copied at full size, and to the file's shown size,
        // since the size passed in may include the decoder's padding.
        double shownWidth = videoWidth > 0 ? videoWidth : sourceWidth;
        double shownHeight = videoHeight > 0 ? videoHeight : sourceHeight;
        var scale = Math.Min(1.0, Math.Min(maxWidth / shownWidth, maxHeight / shownHeight));
        width = Math.Max(2, (int)Math.Round(shownWidth * scale) & ~1);
        height = Math.Max(2, (int)Math.Round(shownHeight * scale) & ~1);
        stride = width * 4;
        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
        sourceWidth = (uint)width;
        sourceHeight = (uint)height;
        pitches = (uint)stride;
        lines = (uint)height;
        buffer = AllocateBuffer((nuint)(stride * height));
        return 1;
    }

    private void OnCleanup(ref IntPtr opaque)
    {
        FreeBuffer(buffer);
        buffer = IntPtr.Zero;
    }

    private IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, buffer);
        return IntPtr.Zero;
    }

    private void OnDisplay(IntPtr opaque, IntPtr picture)
    {
        if (buffer == IntPtr.Zero)
        {
            return;
        }

        unsafe
        {
            sink.OnFrame(new VideoFrame(new ReadOnlySpan<byte>((void*)buffer, stride * height), width, height, stride));
        }

        bool first;
        lock (gate)
        {
            first = !shownFirstFrame && path is not null;
            shownFirstFrame = true;
        }

        if (first)
        {
            RaiseStateChanged();
        }
    }

    private static unsafe IntPtr AllocateBuffer(nuint size) => (IntPtr)NativeMemory.Alloc(size);

    private static unsafe void FreeBuffer(IntPtr pointer) => NativeMemory.Free((void*)pointer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LibVLC reported a playback error")]
    private static partial void LogPlaybackError(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No video frame within {Seconds} s; the preview offers the default app")]
    private static partial void LogNoFrame(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A LibVLC request failed")]
    private static partial void LogCommandFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LibVLC didn't stop within {Seconds} s; leaving it to finish in the background")]
    private static partial void LogDisposeTimedOut(ILogger logger, double seconds);
}
