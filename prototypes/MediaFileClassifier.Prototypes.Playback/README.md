# Playback spike prototype (MFC-54)

A throwaway preview player for the media playback spike (E14-SP01). Findings and D070 are in [docs/spikes/media-playback.md](../../docs/spikes/media-playback.md). Nothing in `src/` references this project.

## Run it

From the repository root:

```powershell
dotnet run --project prototypes/MediaFileClassifier.Prototypes.Playback
```

The window lists the files in `tests/SampleMedia`, in `%TEMP%\mfc-playback-media` if it exists, and in any folders passed after `--`. Pick a file and an engine:

| Engine | What it does |
|---|---|
| LibVLC → Avalonia bitmap | LibVLC decodes (with hardware decoding) and plays the audio. Frames are copied into a `WriteableBitmap`, so the overlay stays visible. |
| LibVLC native view | `LibVLCSharp.Avalonia.VideoView`: VLC draws into a native child window with Direct3D 11. Shows the airspace problem: the overlay is hidden behind the video. |
| FFmpeg pipe | The local `ffmpeg` (D068) decodes and scales, and sends raw frames over stdout. Video only, no audio. |
| FFmpeg pipe, D3D11VA | The same with `-hwaccel d3d11va`. |

GIF and WebP files always open in `Avalonia.Labs.AnimatedImage`. **Open in default app (D026)** pauses, copies the current timestamp to the clipboard, and opens the file in the OS default app.

## Measure

The measurements need longer clips with H.265, AV1 and 4K, which are too large to commit. Generate them once (needs Python 3 and an ffmpeg build with libx264, libx265, libsvtav1 and libwebp):

```powershell
python prototypes/MediaFileClassifier.Prototypes.Playback/generate_playback_media.py $env:TEMP/mfc-playback-media
```

Then run every engine on every clip; the Markdown report goes to stdout:

```powershell
dotnet run -c Release --project prototypes/MediaFileClassifier.Prototypes.Playback -- measure
```

Each frame of the generated clips carries its frame number as a binary code in the band at the top, so seek accuracy is measured exactly. The run plays audio (a 440 Hz tone) and briefly opens a VLC window for the `vlc-native` rows.
