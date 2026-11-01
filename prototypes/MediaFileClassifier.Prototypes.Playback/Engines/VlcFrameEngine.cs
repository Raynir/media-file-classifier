using System.Globalization;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace MediaFileClassifier.Prototypes.Playback.Engines;

/// <summary>
/// LibVLC decodes and plays audio; each video frame is copied into memory (<c>vmem</c> callbacks) and
/// drawn by Avalonia. No native child window, so nothing in the Avalonia tree is hidden (no airspace issue).
/// </summary>
internal sealed unsafe class VlcFrameEngine : PreviewEngine
{
    private readonly LibVLC libVlc;
    private readonly MediaPlayer player;
    private readonly IFrameSink sink;
    private readonly int maxWidth;
    private readonly int maxHeight;

    // LibVLC holds these delegates as native function pointers; the fields keep them alive.
    private readonly MediaPlayer.LibVLCVideoFormatCb formatCallback;
    private readonly MediaPlayer.LibVLCVideoCleanupCb cleanupCallback;
    private readonly MediaPlayer.LibVLCVideoLockCb lockCallback;
    private readonly MediaPlayer.LibVLCVideoDisplayCb displayCallback;

    private void* buffer;
    private int width;
    private int height;
    private int stride;

    public VlcFrameEngine(LibVLC libVlc, IFrameSink sink, int maxWidth, int maxHeight, string name)
    {
        this.libVlc = libVlc;
        player = new MediaPlayer(libVlc);
        this.sink = sink;
        this.maxWidth = maxWidth;
        this.maxHeight = maxHeight;
        Name = name;
        formatCallback = OnFormat;
        cleanupCallback = OnCleanup;
        lockCallback = OnLock;
        displayCallback = OnDisplay;
        player.SetVideoFormatCallbacks(formatCallback, cleanupCallback);
        player.SetVideoCallbacks(lockCallback, null, displayCallback);
    }

    public override string Name { get; }

    public override TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, player.Time));

    public override TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, player.Length));

    public override bool IsPlaying => player.IsPlaying;

    public override void Open(string path, TimeSpan start)
    {
        using var media = new Media(libVlc, path, FromType.FromPath);
        if (start > TimeSpan.Zero)
        {
            media.AddOption(":start-time=" + start.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        player.Media = media;
        player.Play();
    }

    public override void Play() => player.SetPause(false);

    public override void Pause() => player.SetPause(true);

    public override void Seek(TimeSpan position) => player.Time = (long)position.TotalMilliseconds;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            player.Stop();
            player.Dispose();
        }

        base.Dispose(disposing);
    }

    private uint OnFormat(ref IntPtr opaque, IntPtr chroma, ref uint sourceWidth, ref uint sourceHeight, ref uint pitches, ref uint lines)
    {
        // Ask VLC to scale to the preview size, so a 4K source isn't copied at full size.
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight));
        width = (int)(sourceWidth * scale) & ~1;
        height = (int)(sourceHeight * scale) & ~1;
        stride = width * 4;
        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
        sourceWidth = (uint)width;
        sourceHeight = (uint)height;
        pitches = (uint)stride;
        lines = (uint)height;
        buffer = NativeMemory.Alloc((nuint)(stride * height));
        return 1;
    }

    private void OnCleanup(ref IntPtr opaque)
    {
        NativeMemory.Free(buffer);
        buffer = null;
    }

    private IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, (IntPtr)buffer);
        return IntPtr.Zero;
    }

    private void OnDisplay(IntPtr opaque, IntPtr picture) =>
        sink.OnFrame(new VideoFrame(new ReadOnlySpan<byte>(buffer, stride * height), width, height, stride));
}
