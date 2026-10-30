using System.Globalization;
using LibVLCSharp.Shared;

namespace MediaFileClassifier.Prototypes.Playback.Engines;

/// <summary>
/// LibVLC renders straight to a native window with its GPU output (Direct3D 11 on Windows).
/// In the window that is <c>LibVLCSharp.Avalonia.VideoView</c>, a <c>NativeControlHost</c>.
/// </summary>
internal sealed class VlcNativeEngine(LibVLC libVlc) : PreviewEngine
{
    public MediaPlayer Player { get; } = new(libVlc);

    public override string Name => "vlc-native";

    public override TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, Player.Time));

    public override TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, Player.Length));

    public override bool IsPlaying => Player.IsPlaying;

    public override void Open(string path, TimeSpan start)
    {
        using var media = new Media(libVlc, path, FromType.FromPath);
        if (start > TimeSpan.Zero)
        {
            media.AddOption(":start-time=" + start.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        Player.Media = media;
        Player.Play();
    }

    public override void Play() => Player.SetPause(false);

    public override void Pause() => Player.SetPause(true);

    public override void Seek(TimeSpan position) => Player.Time = (long)position.TotalMilliseconds;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Player.Stop();
            Player.Dispose();
        }

        base.Dispose(disposing);
    }
}
