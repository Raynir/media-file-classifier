# Spike: media playback component (MFC-54, E14-SP01)

**Blocks:** E14-S06 Video playback (MFC-117), E14-S12 Animated image playback (MFC-118). **Decision:** D070, accepted by the user; the entry for DECISIONS.md is at the end. **Status:** measured on Windows. Android is assessed from documentation only and is confirmed in E20.

## Question

Which component plays video, and which renders animated GIF and WebP, in Avalonia 12: on desktop now and on Android later? The candidates:

- **A. LibVLCSharp** with its Avalonia integration.
- **B. An FFmpeg-based renderer** on the local FFmpeg install (D068).
- **C. Platform media players** behind an interface.
- **D. An animated-image control** for GIF and WebP.

## Recommendation

**Video: LibVLC through LibVLCSharp, with frames drawn by an Avalonia control (A, "frames" route). Animated images: `Avalonia.Labs.AnimatedImage` (D). Don't build an FFmpeg player (B), and don't take a platform player now (C).**

- **LibVLC plays every format we need, with audio, from one dependency.** H.264, H.265 and AV1 (dav1d) all played at 30 fps on the prototype. It doesn't need the local FFmpeg, which the MVP doesn't bundle (D068), and it doesn't depend on the Windows Store codec extensions.
- **The frames route has no airspace problem.** LibVLC decodes into memory and an ordinary Avalonia control draws the frames, so overlays, the timeline and rounded corners work as usual. The native `VideoView` route works on Avalonia 12, but it hides anything drawn over the video (screenshot check in the prototype).
- **The price of the frames route is CPU.** LibVLC 3 decodes in software when it renders to memory. At 1080p that's 0.2–0.35 of a core; 4K H.265 takes about 0.7 of a core on the test machine. The native route decodes on the GPU and needs about 0.05 of a core, so it stays available behind the same interface if E14-S06 measures too much CPU on the D031 tier.
- **Seeking is accurate enough for "jump to the moment".** LibVLC lands 0–4 frames (at most 133 ms) before the requested time. FFmpeg is frame-exact, but that doesn't justify writing a player around it (below).
- **Animated images use the Avalonia Labs control.** It's MIT, from AvaloniaUI, built for Avalonia 12, and it decodes with SkiaSharp, which the app already ships. It decodes every frame up front, so E14-S12 needs a memory cap and falls back to D026 above it.
- **Size and licence are manageable.** Trimmed to the plugins that local playback needs, LibVLC is 35 MB on win-x64, down from 108 MB per architecture, and it's all LGPL-2.1-or-later. The four GPL plugins in the package are left out.

### Route per platform

| Platform | Video | Animated GIF and WebP | D026 fallback (open in the OS default app, timestamp shown and copied) |
|---|---|---|---|
| **Windows (MVP)** | In-app: LibVLC, frames route. The native `VideoView` route is the fallback if the CPU cost is too high on the D031 tier. | In-app: `Avalonia.Labs.AnimatedImage` | Per item: when LibVLC can't open or decode a file (error event, or no frame within a few seconds), and on the user's request (**Open in default app**). Animated images over the memory cap also use it. |
| **Android (E20)** | **Planned** in-app: LibVLC (`VideoLAN.LibVLC.Android`), same frames route, unconfirmed on phones. **Until E20 measures it, video uses D026:** it opens in the default player app through an intent, and the timestamp is shown and copied. | In-app: `Avalonia.Labs.AnimatedImage` (Skia, cross-platform) | Default for video until E20, then per item as on Windows. |

The preview's keyframe strip (D026) is independent of all this: it comes from FFmpeg frame extraction during analysis (E06-S02), not from the player.

## Comparison

| | A1. LibVLC, frames route | A2. LibVLC, native `VideoView` | B. FFmpeg pipe renderer | C. Platform players | D. `Avalonia.Labs.AnimatedImage` |
|---|---|---|---|---|---|
| **Version** | LibVLCSharp 3.10.2, LibVLC 3.0.24 | LibVLCSharp.Avalonia 3.10.2 | ffmpeg 7.1.1 (local, D068) | Windows Media Foundation; Android Media3 ExoPlayer | 12.1.0 |
| **Avalonia 12** | Yes (only `LibVLCSharp`, no UI package) | Yes: built against Avalonia 11.3.13, but runs on 12.1.4. Needs an app manifest with `supportedOS` (otherwise `NativeControlHost` throws), and `Play` must wait until the view has its window. | Yes (plain `WriteableBitmap`) | Avalonia's own `MediaPlayer` control (12.0.7) is commercial: the **Pro** tier, €899 per seat per year | Yes, made for 12 |
| **H.264 / H.265 / AV1** | All three, measured | All three, measured | All three, measured (whatever the installed build has) | Windows: H.265 needs the HEVC Video Extensions from the Store (paid unless the device came with it), AV1 needs the AV1 extension. Android: depends on the device's decoders. | — |
| **Audio** | Yes | Yes | **No.** Audio and A/V sync would be ours to build. | Yes | — |
| **Seek accuracy** | 0 to −4 frames, median about −1 | Same seek engine as A1 | Exact (input seeking decodes forward from the keyframe) | Not measured | — |
| **CPU at 1080p / 4K** | 0.18–0.35 / 0.7 cores (software decode) | 0.03–0.05 / 0.04 cores (GPU decode) | 0.36–0.47 / 0.8 cores; with D3D11VA 0.2–0.33 / 0.45 | Expected low (GPU), not measured | GIF ≈ 1 ms per frame, WebP ≈ 10 ms per frame at 320×240 |
| **Airspace** | None | **Yes:** nothing in the Avalonia tree draws over the video | None | Yes for a native view; none for a frame-server mode, which needs D3D interop | None |
| **Android** | `VideoLAN.LibVLC.Android` exists; the frames route is portable. Not measured on a phone. | `LibVLCSharp.Avalonia` has no Android view | Deferred with FFmpeg (D068) | ExoPlayer would need its own Android view (airspace) | Yes (Skia) |
| **Licence** | LGPL-2.1-or-later | LGPL-2.1-or-later | LGPL or GPL, depending on the build (D068) | OS components; Avalonia's control is commercial | MIT |
| **Size (win-x64)** | 35 MB trimmed (108 MB untrimmed) + 0.25 MB managed | + 16 KB | 0 shipped (user-installed) | 0 | 40 KB |
| **Verdict** | **Recommended for video** | Kept as the low-CPU option behind the same interface | Rejected as a player; stays for analysis | Not now | **Recommended for animated images** |

## How it was measured

The prototype is [`prototypes/MediaFileClassifier.Prototypes.Playback`](../../prototypes/MediaFileClassifier.Prototypes.Playback/README.md). It runs with one command from the repository root:

```powershell
dotnet run --project prototypes/MediaFileClassifier.Prototypes.Playback
```

`-- measure` prints the measurements below. They need the generated clips; the README has the one-line generator command.

- **Clips:** `tests/SampleMedia` holds a 2 s H.264 clip, a static WebP and two small GIFs. The measurement needed more, generated by `generate_playback_media.py`: 20 s 1080p30 clips in H.264 (x264), H.265 (x265) and AV1 (SVT-AV1), a 10 s 4K H.265 clip, all with AAC audio and a keyframe every 4 s, plus an animated WebP and a larger GIF. Every frame carries its number as a binary code in a band at the top, so the prototype reads back exactly which frame it got.
- **Engines:** `vlc-frames` (A1), `vlc-frames-sw` (A1 with hardware decoding forced off), `vlc-native` (A2, VLC's Direct3D 11 output), `ffmpeg-pipe` and `ffmpeg-pipe-hw` (B, raw BGRA over stdout, `-re` pacing, D3D11VA for `-hw`). Frame routes scale to 1280×720, the preview size.
- **Playback:** 6 s from the start. CPU is process CPU time over wall time, in cores, and includes the `ffmpeg` child. GPU is the Windows "GPU Engine" counters (what Task Manager shows), averaged over the run for the app and `ffmpeg`.
- **Seeks:** seven targets per clip (1.0, 2.5, 5.1, 7.77, 9.9, 13.3 and 17.03 s), some far from a keyframe. **Open at time** opens a fresh player at the timestamp ("jump to the moment"). **Paused seek** pauses on the first frame and seeks, as scrubbing does. The error is the frame shown minus the frame at that time, `floor(t × 30)`. A target between two frames (7.77 s, 17.03 s) can fairly show either neighbour, so ±1 there is not an error.
- **Machine:** Intel Core i7-14700HX (28 logical processors), NVIDIA GeForce RTX 4070 Laptop GPU and Intel UHD Graphics, Windows 11, .NET 10, Avalonia 12.1.4, with the trimmed LibVLC. This is much faster than the D031 tier (4–8 cores); expect roughly 1.5–2 times the CPU there.

## Results

Latency is in ms. Memory is the growth of the app's private bytes; for FFmpeg the decoding happens in the `ffmpeg` process, which isn't counted. `vlc-frames-sw` matched `vlc-frames` within noise on every clip (see below), so it's left out.

| Clip | Engine | First frame | CPU (cores) | GPU 3D / decode (%) | Memory (MB) | Open-at-time error (frames) | Open at time | Paused-seek error (frames) | Paused seek |
|---|---|--:|--:|--:|--:|---|--:|---|--:|
| H.264 1080p | vlc-frames | 22 | 0.20 | 0 / 0 | 171 | −2 −4 −3 −1 −2 −1 0 | 52 | −2 −4 −3 −2 −4 −1 −2 | 52 |
| | vlc-native | 300 | 0.04 | 3 / 28 | 161 | as vlc-frames | | | |
| | ffmpeg-pipe | 261 | 0.43 | 0 / 0 | – | 0 0 0 +1 0 0 +1 | 167 | 0 0 0 +1 0 0 +1 | 121 |
| | ffmpeg-pipe-hw | 356 | 0.33 | 4 / 27 | – | 0 0 0 +1 0 0 +1 | 394 | 0 0 0 +1 0 0 +1 | 327 |
| H.265 1080p | vlc-frames | 34 | 0.26 | 0 / 0 | 208 | −2 −2 −1 0 −2 −1 0 | 98 | −2 −2 −1 0 −4 −1 −2 | 81 |
| | vlc-native | 587 | 0.05 | 0 / 3 | 150 | as vlc-frames | | | |
| | ffmpeg-pipe | 148 | 0.47 | 0 / 0 | – | 0 0 0 +1 0 0 +1 | 179 | 0 0 0 +1 0 0 +1 | 136 |
| | ffmpeg-pipe-hw | 375 | 0.24 | 4 / 8 | – | 0 0 0 +1 0 0 +1 | 351 | 0 0 0 +1 0 0 +1 | 309 |
| AV1 1080p | vlc-frames | 31 | 0.18 | 0 / 0 | 152 | −1 −1 −1 0 −1 −1 0 | 78 | −1 −1 −1 0 −1 −1 0 | 60 |
| | vlc-native | 558 | 0.03 | 0 / 3 | 122 | as vlc-frames | | | |
| | ffmpeg-pipe | 117 | 0.39 | 0 / 0 | – | 0 0 0 +1 0 0 +1 | 128 | 0 0 0 +1 0 0 +1 | 94 |
| | ffmpeg-pipe-hw | 287 | 0.21 | 2 / 3 | – | 0 0 0 +1 0 0 +1 | 361 | 0 0 0 +1 0 0 +1 | 325 |
| H.265 4K | vlc-frames | 46 | 0.69 | 0 / 0 | 695 | −4 −1 −1 −1 | 206 | −4 −1 −3 −3 | 163 |
| | vlc-native | 620 | 0.04 | 1 / 3 | 380 | as vlc-frames | | | |
| | ffmpeg-pipe | 200 | 0.83 | 0 / 0 | – | 0 0 0 +1 | 312 | 0 0 0 +1 | 260 |
| | ffmpeg-pipe-hw | 387 | 0.45 | 3 / 8 | – | 0 0 0 +1 | 610 | 0 0 0 +1 | 567 |

Every engine delivered 30 fps with every frame in order (180 of 180 in 6 s), except that `ffmpeg-pipe` on AV1 ran a few frames ahead at the start (189/180), because `-re` paces by input timestamps.

### Animated images

SkiaSharp's `SKCodec` decodes them, which is what `Avalonia.Labs.AnimatedImage` does internally.

| File | Size | Frames | Loop (ms) | Decode all frames (ms) | Decoded size (MB) |
|---|---|--:|--:|--:|--:|
| `gif-animated.gif` (corpus) | 96×96 | 8 | 800 | 1.0 | 0.3 |
| `animated-320.gif` | 320×240 | 30 | 3,000 | 35.8 | 8.8 |
| `animated.webp` | 320×240 | 30 | 3,000 | 306.4 | 8.8 |
| `gif-static.gif`, `webp.webp` (corpus) | | 1 | – | ≈ 1 | 0.3 |

## Findings

### LibVLC on Avalonia 12

- `LibVLCSharp` itself has no UI dependency. The frames route only needs `MediaPlayer.SetVideoFormatCallbacks` and `SetVideoCallbacks` (RV32, scaled by VLC to the preview size), and a control that copies the newest frame into a `WriteableBitmap`. The prototype's `FrameSurface` drops frames when the UI thread falls behind, never queues them.
- `LibVLCSharp.Avalonia` 3.10.2 declares Avalonia ≥ 11.3.13 and has no Avalonia 12 build, yet its `VideoView` works on 12.1.4. Two conditions apply:
  - The executable needs a manifest with the Windows 10/11 `supportedOS` entry. Without it, `NativeControlHost` throws "Unable to create child window". The app's `Desktop/app.manifest` already has it.
  - `Play` must run after the view has created its native window, e.g. posted to the dispatcher after the view is attached. Played earlier, VLC opens its own top-level window.
- **Airspace:** with `VideoView`, the overlay label drawn over the video was hidden. With the frames route it showed normally. Avalonia popups and flyouts are separate windows, so they'd still show over a native view, but anything in the visual tree wouldn't.
- **Hardware decoding needs the native route.** With frame callbacks, LibVLC 3 decodes in software whatever `--avcodec-hw` says (`any`, `dxva2` and `none` all measured the same, with 0% GPU decode). The native Direct3D 11 output decodes on the GPU. LibVLC 4 promises GPU decoding with custom outputs, but it isn't released.
- **Memory:** the frames route grew the process by 150–210 MB at 1080p and 700 MB for 4K H.265. The native route used 120–180 MB and 380 MB. Most of it is VLC's decoder picture pool at the source resolution, not the 1280×720 copy. E14-S06 should check 4K memory on an 8 GB machine.
- **Start-up:** with the full plugin set, VLC took about 200 ms to the first frame, most of it loading 300 plugins. Trimmed, it takes 20–50 ms. The native route's 300–600 ms includes creating the Direct3D device and window.

### Seek accuracy

- LibVLC lands up to 4 frames early (−133 ms at 30 fps), never late. The error doesn't grow with the distance from the keyframe (9.9 s is 57 frames past one), so it isn't keyframe snapping; the cause wasn't investigated. For "jump to the moment" (timestamps shown to the second) it's invisible. Frame-exact stills belong to analysis, which uses FFmpeg (E06-S02).
- FFmpeg's input seeking (`-ss` before `-i`) was exact on every clip.

### Why not an FFmpeg player (B)

- **No audio.** A player would need an audio output, a clock and A/V sync, which LibVLC already has.
- **FFmpeg isn't guaranteed to be there.** D068 doesn't bundle it in the MVP, so without the local install, playback would be gone too, not only analysis.
- **Seeking restarts the process.** In-process decoding through FFmpeg.AutoGen would avoid that, but it needs the shared-library build, and the usual user installs (the gyan.dev "full" and "essentials" builds) are static executables.
- **It cost more CPU than LibVLC** at the same output size: 0.4–0.5 versus 0.2–0.3 cores at 1080p. Part of that is probably the raw-frame copy through the pipe; it wasn't broken down.
- **A side finding for E08:** the Chocolatey `ffmpeg.exe` on `PATH` is a shim that runs the real executable as a child. Killing the shim left `ffmpeg` running and the pipes open, which hung the first measurement run. Code that stops `ffmpeg` must kill the process tree. This is noted in `docs/native-dependencies.md`.

### Why not a platform player now (C)

- On Windows, Media Foundation's codec coverage depends on Store extensions. H.265 needs HEVC Video Extensions, which are paid unless the device shipped with them, so H.265 playback would vary between users, while LibVLC brings its own decoders.
- Avalonia's own cross-platform `MediaPlayer` (Media Foundation on Windows, ExoPlayer on Android) is a commercial component (the Pro tier, €899 per seat per year). Choosing it would be a product decision, so it's listed here, not recommended.
- Writing our own wrapper (WinRT `MediaPlayer` in frame-server mode with Direct3D interop on Windows, ExoPlayer on Android) means two players to build and maintain.
- It wasn't prototyped. It becomes a candidate again if LibVLC fails on Android in E20, for ExoPlayer only.

### Animated images (D)

- `Avalonia.Labs.AnimatedImage` 12.1.0 played the corpus GIF, the generated GIF and the animated WebP on Avalonia 12 in the prototype. Its source comes from `IAnimatedBitmap.Load(stream)`.
- It decodes **every frame up front** into Avalonia bitmaps: width × height × 4 bytes × frames. That's 8.8 MB for 320×240 × 30, but a 1080p GIF with 300 frames would be 2.4 GB. E14-S12 should check the decoded size from `SKCodec.Info` and `FrameCount` before loading. Over a cap, say 256 MB, it shows the first frame and offers D026, or it gets a streaming decoder that keeps a few frames (the same `SKCodec` calls).
- WebP decodes about 10 times slower than GIF (10 ms against 1.2 ms per 320×240 frame), so loading belongs off the UI thread, which the control already does.
- It's an Avalonia **Labs** package: MIT, but without API stability guarantees. If it changes or stalls, the control is small enough to vendor (8 files).

### Android (not measured)

- `VideoLAN.LibVLC.Android` 3.6.5 is the matching native package (83 MB `.nupkg` for all ABIs; roughly 20–25 MB per ABI in an APK). LibVLCSharp targets `net10.0-android`. `LibVLCSharp.Avalonia`'s `VideoView` has no Android implementation, so Android would use the frames route.
- The risk is CPU and battery: software decoding and a frame copy on a phone. E20 measures it, the way MFC-18 does for vector search. If it fails, ExoPlayer in a native view becomes the Android route.
- Until then, Android video follows D026. Animated images work in-app, since the Labs control only needs Skia.

## Follow-ups

- **E14-S06 (video):**
  - Put `IMediaPreviewPlayer` (open at a timestamp, play, pause, seek, position, duration, and failure events) in `Infrastructure.Abstractions`, with a LibVLC implementation in an `Infrastructure.*` project.
  - The frame-drawing control goes in `Ui`.
  - Add the `LibVLC` probe and the playback capability flag (`docs/native-dependencies.md`).
  - Measure CPU and 4K memory on a 4-core D031 machine, and decide whether the native route is needed as an option.
- **E14-S12 (animated images):** the decoded-size cap with the D026 fallback.
- **E14-S13-T02 (D026):** the prototype's **Open in default app** button does it already: it pauses, copies `m:ss` to the clipboard, and opens the file with the shell.
- **E19 (installer):**
  - Ship only the trimmed win-x64 plugin set.
  - Recheck each plugin's licence.
  - Add the LGPL notice and the source offer for LibVLC and LibVLCSharp.
  - Check patent-licensing questions for H.265 and AAC decoding with counsel.
- **E20 (Android):** measure the frames route on a mid-range phone, and confirm the Android route in D070.

---

## D070 (for DECISIONS.md)

> **D070 — Media playback: LibVLC for video, Avalonia Labs AnimatedImage for GIF and WebP**
> **Status:** Accepted (from E14-SP01, MFC-54; decided by the user on 2026-10-30)
>
> - **Video (Windows, MVP):** in-app playback uses **LibVLC 3 through LibVLCSharp** (LGPL-2.1-or-later). Frames are rendered into memory and drawn by an Avalonia control (the "frames route"), so the preview has no airspace limits. The cost is software decoding: about 0.2–0.35 of a core at 1080p on the test machine.
>   - The player sits behind an `IMediaPreviewPlayer` interface in `Infrastructure.Abstractions`.
>   - LibVLC's native `VideoView` (GPU decoding, airspace) stays available behind that interface, in case E14-S06 measures too much CPU on the D031 tier.
> - **Animated GIF and WebP (all platforms):** `Avalonia.Labs.AnimatedImage` (MIT, SkiaSharp decoding). Above a decoded-size cap, the preview shows the first frame and offers D026.
> - **Not chosen:**
>   - A player built on FFmpeg: no audio or A/V sync, and FFmpeg isn't bundled in the MVP (D068).
>   - Platform players: H.265 on Windows depends on a paid Store extension, and Avalonia's `MediaPlayer` control is commercial.
> - **Packaging:** ship LibVLC (`VideoLAN.LibVLC.Windows`) for win-x64 only, trimmed to the access, demux, codec, output and audio plugins that local files need. That's 35 MB, with no GPL plugins.
> - **Fallback (D026):**
>   - **Windows:** video opens in the OS default app, with the timestamp shown and copied, when LibVLC fails on an item, and on the user's request.
>   - **Android:** video uses D026 until E20 confirms LibVLC's frames route on phones. If it fails, Media3 ExoPlayer is reconsidered.
> - **Seek accuracy:** LibVLC lands up to 4 frames before the target. That's accepted for "jump to the moment"; frame-exact stills come from FFmpeg in analysis.
>
> Details: `docs/spikes/media-playback.md` in the app repository. See E14-S06, E14-S12, E14-S13, E19, E20, D026, D068.
