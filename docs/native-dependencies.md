# Native dependencies

The native binaries the app needs: which features use each one, how it is obtained on each platform, and how the app checks for it at runtime (E01-S08). Licenses are listed in [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

- **Platforms:** Windows 11 x64 is the MVP target (D028). Android comes with E20; until then, the Android column records the expected approach, not a decision.
- **Sizes:** measured for the current versions on win-x64 where the package is referenced. Sizes marked "≈" are estimates for dependencies that aren't referenced yet.

## Matrix

| Dependency | Needed by | Windows (MVP) | Android (E20) | Size on Windows | Runtime check |
|---|---|---|---|---|---|
| **SQLite** (`e_sqlite3`, through Microsoft.Data.Sqlite and SQLitePCLRaw) | Library database (E01-S03) and everything stored in it | NuGet `SQLitePCLRaw.bundle_e_sqlite3`, copied next to the app | Same bundle; it ships Android ABIs | 1.9 MB (`e_sqlite3.dll` 2.1.13) | Planned with E01-S03 (open `:memory:`, `select sqlite_version()`). The app can't run without it, so it has no capability flag. |
| **sqlite-vec** (only if SP01 chooses it, MFC-17) | Vector search | Loadable extension from the `sqlite-vec` release or NuGet, next to the app | Prebuilt Android `.so` from the release | ≈ 0.2–1 MB | Planned with the vector store: `load_extension`, then `select vec_version()`. Flag for semantic search. |
| **ONNX Runtime** (`onnxruntime.dll`, plus `DirectML.dll` for GPU) | Embeddings, tagging, captioning (E06) | NuGet `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4 (DirectML and CPU providers, E06-S08) | NuGet `Microsoft.ML.OnnxRuntime` 1.31.1 (Android AAR; the CPU provider is used, see DECISIONS.md, Android CPU provider) | 35.9 MB: `onnxruntime.dll` 17.3 MB, `DirectML.dll` 1.15.4 18.5 MB (the CPU-only build was 17.3 MB, so DirectML adds 18.5 MB) | **Implemented:** a model whose runtime doesn't load reports `NativeLibraryMissing`. The execution provider is chosen from the hardware info, with a CPU fallback (see below). |
| **FFmpeg** (`ffmpeg`, `ffprobe` executables) | Video metadata, keyframes, audio extraction for transcription | Not bundled for the MVP. A documented local install: on `PATH`, or in an `ffmpeg` folder next to the app (D068, tentative) | Deferred to E20 (probably Android's MediaExtractor/MediaCodec instead of FFmpeg) | Not shipped; a typical build is 80–150 MB per executable | **Implemented:** `FfmpegProbe` runs `ffmpeg -version` and checks for `ffprobe`. It sets the **Video analysis** flag. `VideoMetadataExtractor` runs `ffprobe` through FFMpegCore when the flag is on; otherwise videos get file-level metadata and a `FfmpegUnavailable` warning (E03-S03). |
| **LibVLC** (`libvlc.dll`, `libvlccore.dll`, plugins), through LibVLCSharp (D070) | In-app video playback in the preview (E14-S06) | NuGet `VideoLAN.LibVLC.Windows`, trimmed to win-x64 and the playback plugins (see below) | NuGet `VideoLAN.LibVLC.Android` (native libraries per ABI); confirmed in E20 | 35 MB trimmed; 108 MB untrimmed per architecture (3.0.24) | Planned with E14-S06: create a `LibVLC` instance and log `LibVLC.Version`. Flag for in-app video playback; without it, video opens in the default app (D026). |
| **Whisper.net native runtimes** (`whisper.dll`, `ggml*.dll`) | Speech-to-text for video and audio (E08) | NuGet `Whisper.net.Runtime` (CPU); optionally `.Cuda` or `.Vulkan` | NuGet `Whisper.net.Runtime` ships Android ABIs | ≈ 3–5 MB CPU; the CUDA runtime is hundreds of MB. Models are downloaded separately (75 MB – 3 GB). | Planned with E08: load the runtime, and report the loaded runtime library. Flag for transcription. |
| **Tesseract** and **Leptonica** (`tesseract*.dll`, `leptonica*.dll`), plus the `tessdata` language files | OCR (E07) | NuGet `Tesseract` (wrapper with native libraries); `tessdata` downloaded per language | Tesseract built for Android (e.g. tesseract4android), or ML Kit; decided in E20 | ≈ 5–8 MB of libraries; `tessdata_fast` ≈ 1–4 MB and `tessdata_best` ≈ 10–15 MB per language | Planned with E07: create an engine for each configured language. Flag for OCR, with the missing language in the reason. |
| **SkiaSharp** (`libSkiaSharp.dll`), with **HarfBuzzSharp** and **ANGLE** (`av_libglesv2.dll`) | Rendering of the whole UI (Avalonia); later, thumbnails and image decoding | Comes with Avalonia: `SkiaSharp.NativeAssets.Win32` 3.119.4, `HarfBuzzSharp.NativeAssets.Win32` 8.3.1.3, `Avalonia.Angle.Windows.Natives` | Comes with Avalonia's Android head | 11.6 MB SkiaSharp, 1.8 MB HarfBuzz, 5.4 MB ANGLE | **Implemented:** the version is logged at startup. The UI can't start without it, so it has no capability flag. |

## How the checks work

- At startup, `Program.cs` calls `ICapabilityService.RefreshAsync()`.
- That runs `NativeDependencyCapabilityContributor`, which runs every registered `INativeDependencyProbe`, logs what each one found (version and location), and sets the flags of the features that need it.
- A missing or broken dependency disables only those features. A probe that throws is logged and treated as unavailable, and nothing crashes.
- Flags carry a reason **code**, not text, e.g. `CapabilityReasons.FfmpegNotFound`. The UI turns it into text from `Strings.resx` (E13-S08). Without FFmpeg, a command that needs video analysis is disabled with the tooltip "Video analysis unavailable: FFmpeg not found. …". Technical details, such as error messages, go only to the log.

To add a dependency:

1. Implement `INativeDependencyProbe` next to the code that uses it.
2. List the capabilities that need it. A new capability needs a `Capability_<Name>` string in `Strings.resx`.
3. Report problems with a `CapabilityReasons` code. A new code needs a `CapabilityReason_<Code>` string; `CapabilityTextTests` checks both.
4. Register it with `services.TryAddEnumerable(ServiceDescriptor.Singleton<INativeDependencyProbe, MyProbe>())`.
5. Add a row here and an entry in `THIRD_PARTY_NOTICES.md`.

## FFmpeg on a developer machine

Video features need `ffmpeg` and `ffprobe`. Any recent build works (6.0 or newer). The app looks in two places:

1. An `ffmpeg` folder next to `MediaFileClassifier.Desktop.exe`.
2. Every folder on `PATH`.

Install it with any of these:

```powershell
winget install --id Gyan.FFmpeg.Essentials   # or: choco install ffmpeg, or: scoop install ffmpeg
```

Restart the app after you install it. The log then shows a line like `Native dependency FFmpeg: available, version 7.1.1 …`.

## FFmpeg strategy (D068, tentative)

This section summarizes the proposed decision; DECISIONS.md is the source of truth.

- **MVP developer build (D033):** don't bundle FFmpeg. Use a documented local install, found as described above. The app distributes no FFmpeg binaries, so FFmpeg's license puts no obligations on it yet.
- **Wrapper:** FFMpegCore (MIT) when video features land (E08). It calls the same `ffmpeg` and `ffprobe` executables, so the probe and the wrapper agree on what is installed.
- **Installer (E19):** if FFmpeg is bundled, ship an **LGPL** build: shared libraries, configured without `--enable-gpl` or `--enable-nonfree`. LGPL requires the license notice, a written offer of the exact source, and letting users replace the binaries, which separate executables or DLLs allow. A **GPL** build, such as the common gyan.dev "full" and "essentials" builds that include x264/x265, would make the distributed bundle subject to the GPL, so don't ship one. The probe reports whether the installed build is GPL.
- **Android:** deferred to E20. The platform media APIs may make FFmpeg unnecessary there.

## Video playback (D070)

The playback spike (MFC-54, [docs/spikes/media-playback.md](spikes/media-playback.md)) chose LibVLC for in-app video (D070). Only the prototype in `prototypes/` references it so far.

- **Trimming:** the `VideoLAN.LibVLC.Windows` package copies 108 MB of plugins for each of x64, x86 and arm64. The prototype's project file shows the trimmed set: win-x64 only, and only the access, demux, codec, output and audio plugins that local MP4, MKV, WebM, AVI and Ogg files need. That's 35 MB, and VLC starts faster with fewer plugins to load.
- **Licence:** `libvlc`, `libvlccore` and the kept plugins are LGPL-2.1-or-later. Four plugins in the package are GPL (the x264 encoder, the Lua scripting plugin, and the Dolby Surround and headphone audio filters), and the trimmed set leaves all four out. E19 rechecks the licence of every shipped plugin.
- **Not on FFmpeg:** LibVLC carries its own decoders (libavcodec and dav1d inside its plugins), so playback works without the local FFmpeg install (D068).
- **Chocolatey shims:** the `ffmpeg.exe` that Chocolatey puts on `PATH` is a shim that starts the real executable as a child process. Killing the shim leaves the real `ffmpeg` running and its pipes open. Code that stops `ffmpeg` must kill the whole process tree (`Process.Kill(entireProcessTree: true)`).

## Hardware and execution provider (E01-S07-T04)

- At startup the app logs the processor count, memory, and graphics adapters. On Windows the adapters come from DXGI.
- From those, it chooses an ONNX execution provider and a default level of parallelism:
  - **DirectML** when a hardware adapter has at least 2 GB of dedicated memory.
  - **CPU** otherwise.
  - **Default parallelism:** half the logical processors, from 1 to 8.
- `IHardwareInfo.PreferredGpu` is the adapter with the most dedicated memory, so a laptop uses its discrete GPU; DirectML gets its DXGI index as the device ID.
- `OnnxSessionFactory` tries the preferred provider, then the CPU (E06-S08), and logs each choice with its reason:
  - the provider isn't in the runtime (Android, tests);
  - it doesn't initialise (driver, adapter or DLL errors);
  - the model loads but its **first run** fails: `OnnxModelProvider.Run` reloads that file on the CPU and retries once. The file stays on the CPU until the app restarts. A failure after a successful run is an ordinary item failure.
- `Siglip2Options.ExecutionProvider` overrides the choice (null means automatic); the hardware settings (E17-S08) will set it.
- **Packaging:** Desktop ships `Microsoft.ML.OnnxRuntime.DirectML`, whose last release is 1.24.4, so `Microsoft.ML.OnnxRuntime.Managed` is held at 1.24.4. Android and the tests keep the 1.31.1 native runtime, which accepts the older managed API. The Desktop project points `Microsoft.AI.DirectML` at its x64 `DirectML.dll`, because the package copies it only for an x64 `PlatformTarget`; otherwise ONNX Runtime would load the older copy in System32.
- **Later:** a CUDA build for NVIDIA users (`Microsoft.ML.OnnxRuntime.Gpu.Windows`, needs CUDA 12 and cuDNN 9), and Windows ML once it no longer clashes with the managed package. Neither is planned yet.
- NPUs aren't detected yet. DXGI doesn't list them; DXCore would.
