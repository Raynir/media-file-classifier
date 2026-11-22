# Third-party notices

The third-party libraries, native binaries and AI models that Media File Classifier uses, and their licenses (E01-S08, MFC-1026).

- **Coverage:** every package the Windows app (`MediaFileClassifier.Desktop`) ships, checked against `dotnet list package --include-transitive` for version 1.0.0, and every model in `src/MediaFileClassifier.Infrastructure.AI/Models/model-catalog.json`. The dependency matrix is in [docs/native-dependencies.md](docs/native-dependencies.md).
- **Media File Classifier itself** is under the Apache License 2.0 (D504): see `LICENSE`.
- **When to add an entry:** when its package is referenced. An AI model gets its entry when it enters the model catalogue (E06-S12, D030), after its own license is checked, since licenses differ between models.
- **Full license texts:** the release zip carries `LICENSE`, this file and a `licenses/` folder with the LGPL-2.1 text (LibVLC) and the DirectML license. The source is in `tools/publish-release/licenses` (its README says where each text came from). The other licenses are named in the tables and are the ones published with each package.
- **Native binaries in the zip:** the tables cover the Windows x64 build. The packages also bring Linux, WebAssembly and macOS native assets (HarfBuzzSharp, SkiaSharp, Avalonia.Native, X11 and so on) that the Windows zip doesn't contain.

## Shipped with the app

| Component | Version | License | Source | Notes |
|---|---|---|---|---|
| Avalonia (`Avalonia`, `.Desktop`, `.Skia`, `.Win32`, `.Themes.Fluent`, `.HarfBuzz`, `.Remote.Protocol`, `.Native`, `.X11`, `.FreeDesktop`, `.FreeDesktop.AtSpi`) | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia | UI framework |
| Avalonia.Fonts.Inter (Inter typeface) | 12.1.4 | MIT (package), SIL OFL 1.1 (font) | https://github.com/rsms/inter | |
| Avalonia.Angle.Windows.Natives (ANGLE) | 2.1.27548 | BSD-3-Clause | https://chromium.googlesource.com/angle/angle | Native, Windows |
| SkiaSharp, SkiaSharp.NativeAssets.Win32 (Skia) | 3.119.4 | MIT (SkiaSharp), BSD-3-Clause (Skia) | https://github.com/mono/SkiaSharp | Native; see the dependency matrix. Also decodes images for the perceptual hash (E01-S06) |
| HarfBuzzSharp, HarfBuzzSharp.NativeAssets.Win32 (HarfBuzz) | 8.3.1.3 | MIT (HarfBuzzSharp), Old MIT (HarfBuzz) | https://github.com/mono/SkiaSharp | Native |
| MicroCom.Runtime | 0.11.6 | MIT | https://github.com/kekekeks/MicroCom | |
| Tmds.DBus.Protocol | 0.94.1 | MIT | https://github.com/tmds/Tmds.DBus | Brought in by Avalonia for Linux; not used on Windows |
| Google.Protobuf | 3.30.2 | BSD-3-Clause | https://github.com/protocolbuffers/protobuf | Through Avalonia.Remote.Protocol |
| Avalonia.Labs.AnimatedImage | 12.1.0 | MIT | https://github.com/AvaloniaUI/Avalonia.Labs | Animated GIF and WebP |
| LibVLCSharp | 3.10.2 | LGPL-2.1-or-later | https://code.videolan.org/videolan/LibVLCSharp | .NET bindings for video playback (D070). `LibVLCSharp.Avalonia` has the same version and license, and only the playback project and the benchmarks reference it |
| LibVLC (`VideoLAN.LibVLC.Windows`) | 3.0.24 | LGPL-2.1-or-later (libvlc, libvlccore and the kept plugins); a few plugins are GPL and are left out | https://www.videolan.org/vlc/libvlc.html | Native, in the `libvlc` folder of the zip. Dynamically linked, so it can be replaced. License text: `licenses/LGPL-2.1.txt`. Source code: https://code.videolan.org/videolan/libvlc-nuget and https://www.videolan.org/vlc/ |
| DirectML (`Microsoft.AI.DirectML`, a dependency of `Microsoft.ML.OnnxRuntime.DirectML`) | 1.15.4 | Microsoft software license terms (redistributable, proprietary) | https://github.com/microsoft/DirectML | Native `DirectML.dll`: GPU acceleration for the AI models. License text: `licenses/DirectML-LICENSE.txt`, with `licenses/DirectML-ThirdPartyNotices.txt` |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet | |
| Humanizer.Core | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer | Singularises English AI tags (E06-S04-T05) |
| Snowball Russian stemmer (algorithm, ported to C# in `Domain/Text/RussianStemmer.cs`; test vocabulary sample in `Domain.Tests`) | snowball-data master, 2026-11 | BSD-3-Clause (Copyright (c) 2001 Dr Martin Porter) | https://snowballstem.org/algorithms/russian/stemmer.html | Russian keyword search (D349, E06-S04-T10) |
| MediatR, MediatR.Contracts | 12.5.0, 2.0.1 | Apache-2.0 | https://github.com/jbogard/MediatR | Stays on 12.5.0 (D063) |
| Microsoft.Extensions.* (Hosting, DependencyInjection, Logging, Configuration, Options, Caching, Diagnostics, FileProviders, …), System.Diagnostics.EventLog | 10.0.14 | MIT | https://github.com/dotnet/runtime | |
| Microsoft.Extensions.AI.Abstractions, Microsoft.Extensions.AI.OpenAI | 10.11.0 | MIT | https://github.com/dotnet/extensions | The OpenAI-compatible model server provider (D005) is built on them |
| OpenAI (.NET library) | 2.14.0 | MIT | https://github.com/openai/openai-dotnet | Through Microsoft.Extensions.AI.OpenAI. The app sends requests only to a server the user configures |
| System.ClientModel, System.Memory.Data | 1.15.0, 10.0.9 | MIT | https://github.com/Azure/azure-sdk-for-net, https://github.com/dotnet/runtime | Through the OpenAI library |
| Serilog | 4.3.0 | Apache-2.0 | https://github.com/serilog/serilog | |
| Serilog.Extensions.Hosting, Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | https://github.com/serilog/serilog-extensions-hosting | |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | https://github.com/serilog/serilog-sinks-file | |
| Microsoft.Data.Sqlite, Microsoft.Data.Sqlite.Core | 10.0.14 | MIT | https://github.com/dotnet/efcore | |
| Microsoft.EntityFrameworkCore (`.Abstractions`, `.Relational`, `.Sqlite`, `.Sqlite.Core`) | 10.0.14 | MIT | https://github.com/dotnet/efcore | Data access (D071) |
| SQLitePCLRaw (`bundle_e_sqlite3`, `core`, `provider.e_sqlite3`, `lib.e_sqlite3`) | 2.1.13 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw | |
| SQLite (`e_sqlite3`) | bundled by SQLitePCLRaw | Public domain | https://sqlite.org/copyright.html | Native; see the dependency matrix |
| Avalonia.Android | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia | Android head only (MFC-18) |
| AndroidX and Kotlin bindings (`Xamarin.AndroidX.*`, `Xamarin.Kotlin.*`, `Xamarin.Google.*`), transitive through Avalonia.Android | per package | Apache-2.0 | https://github.com/dotnet/android-libraries | Android head only |
| ONNX Runtime (`Microsoft.ML.OnnxRuntime.DirectML`, `Microsoft.ML.OnnxRuntime.Managed`) | 1.24.4 | MIT | https://github.com/microsoft/onnxruntime | Native `onnxruntime.dll`. Runs the AI models on the CPU or, through DirectML, the GPU. Windows stays on 1.24.4 because DirectML's last release is 1.24.4 |
| ONNX Runtime (`Microsoft.ML.OnnxRuntime`) | 1.31.1 | MIT | https://github.com/microsoft/onnxruntime | Native. Android head only (MFC-18); its telemetry provider and network permissions are stripped (D081) |
| Microsoft.ML.Tokenizers | 2.0.1 | MIT | https://github.com/dotnet/machinelearning | Text tokenizers for the models |
| System.IO.Hashing | 10.0.14 | MIT | https://github.com/dotnet/runtime | XxHash128 fingerprints and content hashes (E01-S06) |
| System.Numerics.Tensors | 10.0.14 | MIT | https://github.com/dotnet/runtime | Vector search (D067) |
| MetadataExtractor | 2.9.3 | Apache-2.0 | https://github.com/drewnoakes/metadata-extractor-dotnet | Image metadata (E03-S03) |
| FFMpegCore | 5.5.0 | MIT | https://github.com/rosenbjerg/FFMpegCore | Runs the locally installed `ffprobe` and `ffmpeg` for video metadata and keyframes (D068, E03-S03). FFmpeg itself is not shipped |
| Instances | 3.1.0 | MIT | https://github.com/rosenbjerg/Instances | Process runner, through FFMpegCore |
| XmpCore | 6.1.10.1 | BSD-3-Clause (Adobe XMP Toolkit) | https://github.com/drewnoakes/xmp-core-dotnet | XMP parsing, through MetadataExtractor |

## Build and test only (not shipped)

| Component | Version | License | Source |
|---|---|---|---|
| StyleCop.Analyzers | 1.2.0-beta.556 | MIT | https://github.com/DotNetAnalyzers/StyleCopAnalyzers |
| Riok.Mapperly (source generator) | 4.3.1 | Apache-2.0 | https://github.com/riok/mapperly |
| Avalonia.BuildServices | 11.3.2 | MIT | https://github.com/AvaloniaUI/Avalonia |
| xunit.v3 | 4.0.2 | Apache-2.0 | https://github.com/xunit/xunit |
| Microsoft.EntityFrameworkCore.Design, dotnet-ef (tool) | 10.0.14 | MIT | https://github.com/dotnet/efcore |

## Not shipped yet

Referenced only by benchmarks or prototypes, or not referenced at all. Check each version and license again when its package is added to the app, and move its row to "Shipped with the app".

| Component | License | Source | Notes |
|---|---|---|---|
| sqlite-vec | MIT or Apache-2.0 | https://github.com/asg017/sqlite-vec | 0.1.7-alpha.2.1. The benchmarks project only (SP01, MFC-17) |
| FFmpeg (`ffmpeg`, `ffprobe`) | LGPL-2.1-or-later, or GPL-2.0/3.0 for builds with `--enable-gpl` | https://ffmpeg.org/legal.html | Not distributed: the user installs it (D068). The app runs it as a separate program |
| Whisper.net, Whisper.net.Runtime | MIT | https://github.com/sandrohanea/whisper.net | E08 |
| whisper.cpp, ggml (inside Whisper.net.Runtime) | MIT | https://github.com/ggml-org/whisper.cpp | Native |
| Tesseract (.NET wrapper) | Apache-2.0 | https://github.com/charlesw/tesseract | E07 |
| Tesseract OCR | Apache-2.0 | https://github.com/tesseract-ocr/tesseract | Native |
| Leptonica | BSD-2-Clause (Leptonica license) | http://www.leptonica.org/about-the-license.html | Native, comes with Tesseract |
| tessdata (`tessdata_fast`, `tessdata_best`) | Apache-2.0 | https://github.com/tesseract-ocr/tessdata_best | Language data, downloaded per language |

## AI models

**The app ships no model.** It downloads a model when the user asks (Tools, Settings, AI & Models), or imports it from a folder. Models are stored under `%LOCALAPPDATA%\MediaFileClassifier\models`. The files come from the pinned URLs in `model-catalog.json` and are checked against their SHA-256. The license shown in the app comes from the same catalogue.

| Model | Version or checkpoint | License | Source | Used for |
|---|---|---|---|---|
| SigLIP 2 base patch16-256 (`siglip2-base-patch16-256`) | 1.0; ONNX export by onnx-community; Standard (fp32) and Compact (fp16 text tower) tiers | Apache-2.0 | https://huggingface.co/google/siglip2-base-patch16-256 | Search by description and similar-image search (embeddings) |
| PP-OCRv5 mobile, Eastern European and Slavic languages (`pp-ocrv5-mobile-eslav`) | 1.0 | Apache-2.0 | https://github.com/RapidAI/RapidOCR | Reading text in images (OCR) |
| Florence-2 base fine-tuned (`florence2-base-ft`) | 1.0; ONNX export by onnx-community, fp32 | MIT | https://huggingface.co/onnx-community/Florence-2-base-ft | Descriptions and tags (the default describer) |
| Florence-2 large fine-tuned (`florence2-large-ft`) | 1.0; ONNX export by onnx-community, fp32 | MIT | https://huggingface.co/onnx-community/Florence-2-large-ft | Descriptions and tags (optional, larger) |
