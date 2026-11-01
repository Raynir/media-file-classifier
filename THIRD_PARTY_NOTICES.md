# Third-party notices

The third-party libraries, native binaries and AI models that Media File Classifier uses, and their licenses (E01-S08).

- **Coverage:** every row of the dependency matrix in [docs/native-dependencies.md](docs/native-dependencies.md) is listed here.
- **When to add an entry:** when its package is referenced. An AI model gets its entry when it enters the model catalogue (E06-S12, D030), after its own license is checked, since licenses differ between models, e.g. Florence-2 and the various CLIP checkpoints.
- **Full license texts** ship with the installer (E19). This file is the register.

## Shipped with the app

| Component | Version | License | Source | Notes |
|---|---|---|---|---|
| Avalonia (`Avalonia`, `.Desktop`, `.Skia`, `.Win32`, `.Themes.Fluent`, `.HarfBuzz`, `.Remote.Protocol`, `.Native`, `.X11`, `.FreeDesktop`, `.FreeDesktop.AtSpi`) | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia | UI framework |
| Avalonia.Fonts.Inter (Inter typeface) | 12.1.4 | MIT (package), SIL OFL 1.1 (font) | https://github.com/rsms/inter | |
| Avalonia.Angle.Windows.Natives (ANGLE) | 2.1.27548 | BSD-3-Clause | https://chromium.googlesource.com/angle/angle | Native, Windows |
| SkiaSharp, SkiaSharp.NativeAssets.Win32 (Skia) | 3.119.4 | MIT (SkiaSharp), BSD-3-Clause (Skia) | https://github.com/mono/SkiaSharp | Native; see the dependency matrix |
| HarfBuzzSharp, HarfBuzzSharp.NativeAssets.Win32 (HarfBuzz) | 8.3.1.3 | MIT (HarfBuzzSharp), Old MIT (HarfBuzz) | https://github.com/mono/SkiaSharp | Native |
| MicroCom.Runtime | 0.11.6 | MIT | https://github.com/kekekeks/MicroCom | |
| Tmds.DBus.Protocol | 0.94.1 | MIT | https://github.com/tmds/Tmds.DBus | Used on Linux only |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet | |
| MediatR, MediatR.Contracts | 12.5.0, 2.0.1 | Apache-2.0 | https://github.com/jbogard/MediatR | Stays on 12.5.0 (D063) |
| Microsoft.Extensions.* (Hosting, DependencyInjection, Logging, Configuration, Options, …), System.Diagnostics.EventLog | 10.0.14 | MIT | https://github.com/dotnet/runtime | |
| Serilog | 4.3.0 | Apache-2.0 | https://github.com/serilog/serilog | |
| Serilog.Extensions.Hosting, Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | https://github.com/serilog/serilog-extensions-hosting | |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | https://github.com/serilog/serilog-sinks-file | |
| Microsoft.Data.Sqlite, Microsoft.Data.Sqlite.Core | 10.0.14 | MIT | https://github.com/dotnet/efcore | |
| Microsoft.EntityFrameworkCore (`.Abstractions`, `.Relational`, `.Sqlite`, `.Sqlite.Core`) | 10.0.14 | MIT | https://github.com/dotnet/efcore | Data access (D071) |
| SQLitePCLRaw (`bundle_e_sqlite3`, `core`, `provider.e_sqlite3`, `lib.e_sqlite3`) | 2.1.13 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw | |
| SQLite (`e_sqlite3`) | bundled by SQLitePCLRaw | Public domain | https://sqlite.org/copyright.html | Native; see the dependency matrix |
| Avalonia.Android | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia | Android head only (MFC-18) |
| AndroidX and Kotlin bindings (`Xamarin.AndroidX.*`, `Xamarin.Kotlin.*`, `Xamarin.Google.*`), transitive through Avalonia.Android | per package | Apache-2.0 | https://github.com/dotnet/android-libraries | Android head only |
| ONNX Runtime (`Microsoft.ML.OnnxRuntime`, `.Managed`) | 1.31.1 | MIT | https://github.com/microsoft/onnxruntime | Native. Android head only so far (MFC-18); its telemetry provider and network permissions are stripped (D081) |
| Microsoft.ML.Tokenizers | 2.0.1 | MIT | https://github.com/dotnet/machinelearning | Android head only so far (MFC-18) |
| System.Numerics.Tensors | 10.0.14 | MIT | https://github.com/dotnet/runtime | Android head (MFC-18); vector search (D067) |
| MetadataExtractor | 2.9.3 | Apache-2.0 | https://github.com/drewnoakes/metadata-extractor-dotnet | Image metadata (E03-S03) |
| XmpCore | 6.1.10.1 | BSD-3-Clause (Adobe XMP Toolkit) | https://github.com/drewnoakes/xmp-core-dotnet | XMP parsing, through MetadataExtractor |

## Build and test only (not shipped)

| Component | Version | License | Source |
|---|---|---|---|
| StyleCop.Analyzers | 1.2.0-beta.556 | MIT | https://github.com/DotNetAnalyzers/StyleCopAnalyzers |
| Riok.Mapperly (source generator) | 4.3.1 | Apache-2.0 | https://github.com/riok/mapperly |
| Avalonia.BuildServices | 11.3.2 | MIT | https://github.com/AvaloniaUI/Avalonia |
| xunit.v3 | 4.0.2 | Apache-2.0 | https://github.com/xunit/xunit |
| Microsoft.EntityFrameworkCore.Design, dotnet-ef (tool) | 10.0.14 | MIT | https://github.com/dotnet/efcore |

## Planned (in the dependency matrix, not referenced yet)

Check each version and license again when its package is added, and move its row to "Shipped with the app".

| Component | License | Source | Notes |
|---|---|---|---|
| sqlite-vec | MIT or Apache-2.0 | https://github.com/asg017/sqlite-vec | Only if SP01 chooses it (MFC-17) |
| DirectML (`Microsoft.AI.DirectML`, comes with `Microsoft.ML.OnnxRuntime.DirectML`) | Microsoft DirectML license (redistributable, proprietary) | https://github.com/microsoft/DirectML | Review the redistribution terms before E19 |
| FFmpeg (`ffmpeg`, `ffprobe`) | LGPL-2.1-or-later, or GPL-2.0/3.0 for builds with `--enable-gpl` | https://ffmpeg.org/legal.html | Not distributed in the MVP; the user installs it (D068, tentative) |
| LibVLCSharp, LibVLCSharp.Avalonia | LGPL-2.1-or-later | https://code.videolan.org/videolan/LibVLCSharp | 3.10.2. Referenced by the playback prototype only (MFC-54); in-app video (D070) |
| LibVLC (`VideoLAN.LibVLC.Windows`, `VideoLAN.LibVLC.Android`) | LGPL-2.1-or-later (libvlc, libvlccore and the kept plugins); a few plugins are GPL and are left out | https://www.videolan.org/vlc/libvlc.html | 3.0.24. Native, prototype only; see the dependency matrix |
| Avalonia.Labs.AnimatedImage | MIT | https://github.com/AvaloniaUI/Avalonia.Labs | 12.1.0. Animated GIF and WebP; prototype only (MFC-54) |
| FFMpegCore | MIT | https://github.com/rosenbjerg/FFMpegCore | Wrapper, E08 |
| Whisper.net, Whisper.net.Runtime | MIT | https://github.com/sandrohanea/whisper.net | E08 |
| whisper.cpp, ggml (inside Whisper.net.Runtime) | MIT | https://github.com/ggml-org/whisper.cpp | Native |
| Tesseract (.NET wrapper) | Apache-2.0 | https://github.com/charlesw/tesseract | E07 |
| Tesseract OCR | Apache-2.0 | https://github.com/tesseract-ocr/tesseract | Native |
| Leptonica | BSD-2-Clause (Leptonica license) | http://www.leptonica.org/about-the-license.html | Native, comes with Tesseract |
| tessdata (`tessdata_fast`, `tessdata_best`) | Apache-2.0 | https://github.com/tesseract-ocr/tessdata_best | Language data, downloaded per language |

## AI models

None in the catalogue yet. Add each model when it enters the catalogue (E06-S12, D030) and its license has been checked.

| Model | Version or checkpoint | License | Source | Used for |
|---|---|---|---|---|
