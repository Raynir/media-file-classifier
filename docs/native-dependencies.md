# Native dependencies

The native binaries the app needs: which features use each one, how it is obtained on each platform, and how the app checks for it at runtime (E01-S08). Licenses are listed in [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

- **Platforms:** Windows 11 x64 is the MVP target (D028). Android comes with E20; until then, the Android column records the expected approach, not a decision.
- **Sizes:** measured for the current versions on win-x64 where the package is referenced. Sizes marked "≈" are estimates for dependencies that aren't referenced yet.

## Matrix

| Dependency | Needed by | Windows (MVP) | Android (E20) | Size on Windows | Runtime check |
|---|---|---|---|---|---|
| **SQLite** (`e_sqlite3`, through Microsoft.Data.Sqlite and SQLitePCLRaw) | Library database (E01-S03) and everything stored in it | NuGet `SQLitePCLRaw.bundle_e_sqlite3`, copied next to the app | Same bundle; it ships Android ABIs | 1.9 MB (`e_sqlite3.dll` 2.1.13) | Planned with E01-S03 (open `:memory:`, `select sqlite_version()`). The app can't run without it, so it has no capability flag. |
| **sqlite-vec** (only if SP01 chooses it, MFC-17) | Vector search | Loadable extension from the `sqlite-vec` release or NuGet, next to the app | Prebuilt Android `.so` from the release | ≈ 0.2–1 MB | Planned with the vector store: `load_extension`, then `select vec_version()`. Flag for semantic search. |
| **ONNX Runtime** (`onnxruntime.dll`, plus `DirectML.dll` for GPU) | Embeddings, tagging, captioning (E06) | NuGet `Microsoft.ML.OnnxRuntime` (CPU) or `Microsoft.ML.OnnxRuntime.DirectML` | NuGet `Microsoft.ML.OnnxRuntime` (Android AAR, NNAPI execution provider) | ≈ 12 MB CPU; ≈ 30 MB with DirectML | Planned with E06: create an `InferenceSession` on a tiny model, and list the available providers. The execution provider is chosen from the hardware info (see below). |
| **FFmpeg** (`ffmpeg`, `ffprobe` executables) | Video metadata, keyframes, audio extraction for transcription | Not bundled for the MVP. A documented local install: on `PATH`, or in an `ffmpeg` folder next to the app (D068, tentative) | Deferred to E20 (probably Android's MediaExtractor/MediaCodec instead of FFmpeg) | Not shipped; a typical build is 80–150 MB per executable | **Implemented:** `FfmpegProbe` runs `ffmpeg -version` and checks for `ffprobe`. It sets the **Video analysis** flag. |
| **Whisper.net native runtimes** (`whisper.dll`, `ggml*.dll`) | Speech-to-text for video and audio (E08) | NuGet `Whisper.net.Runtime` (CPU); optionally `.Cuda` or `.Vulkan` | NuGet `Whisper.net.Runtime` ships Android ABIs | ≈ 3–5 MB CPU; the CUDA runtime is hundreds of MB. Models are downloaded separately (75 MB – 3 GB). | Planned with E08: load the runtime, and report the loaded runtime library. Flag for transcription. |
| **Tesseract** and **Leptonica** (`tesseract*.dll`, `leptonica*.dll`), plus the `tessdata` language files | OCR (E07) | NuGet `Tesseract` (wrapper with native libraries); `tessdata` downloaded per language | Tesseract built for Android (e.g. tesseract4android), or ML Kit; decided in E20 | ≈ 5–8 MB of libraries; `tessdata_fast` ≈ 1–4 MB and `tessdata_best` ≈ 10–15 MB per language | Planned with E07: create an engine for each configured language. Flag for OCR, with the missing language in the reason. |
| **SkiaSharp** (`libSkiaSharp.dll`), with **HarfBuzzSharp** and **ANGLE** (`av_libglesv2.dll`) | Rendering of the whole UI (Avalonia); later, thumbnails and image decoding | Comes with Avalonia: `SkiaSharp.NativeAssets.Win32` 3.119.4, `HarfBuzzSharp.NativeAssets.Win32` 8.3.1.3, `Avalonia.Angle.Windows.Natives` | Comes with Avalonia's Android head | 11.6 MB SkiaSharp, 1.8 MB HarfBuzz, 5.4 MB ANGLE | **Implemented:** the version is logged at startup. The UI can't start without it, so it has no capability flag. |

## How the checks work

- At startup, `Program.cs` calls `ICapabilityService.RefreshAsync()`.
- That runs `NativeDependencyCapabilityContributor`, which runs every registered `INativeDependencyProbe`, logs what each one found (version and location), and sets the flags of the features that need it.
- A missing or broken dependency disables only those features. For example, without FFmpeg the **Video analysis** flag reads "Video analysis unavailable: FFmpeg not found. …". A probe that throws is logged and treated as unavailable, and nothing crashes.

To add a dependency:

1. Implement `INativeDependencyProbe` next to the code that uses it.
2. List the capabilities that need it.
3. Register it with `services.TryAddEnumerable(ServiceDescriptor.Singleton<INativeDependencyProbe, MyProbe>())`.
4. Add a row here and an entry in `THIRD_PARTY_NOTICES.md`.

## FFmpeg on a developer machine

Video features need `ffmpeg` and `ffprobe`. Any recent build works (6.0 or newer). The app looks in two places:

1. An `ffmpeg` folder next to `MediaFileClassifier.Desktop.exe`.
2. Every folder on `PATH`.

Install it with any of these:

```powershell
winget install --id Gyan.FFmpeg.Essentials   # or: choco install ffmpeg, or: scoop install ffmpeg
```

Restart the app after you install it. The log then shows a line like `Native dependency FFmpeg: available, version 7.1.1 …`.
