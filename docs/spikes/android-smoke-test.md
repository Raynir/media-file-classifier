# Spike: Android stack smoke test (MFC-18, E01-SP02)

**Blocks:** E20 Android App (MFC-49), E20-S05 Search on the phone (MFC-177). **Decisions:** D080 to D084, accepted by the user; drafts for DECISIONS.md at the end. **Status:** measured on one phone, Release builds. The model is provisional until MFC-53.

## Question

Do the core building blocks run on Android before the architecture is built on them? Finding a blocker now costs days; finding it after the MVP could mean rewriting the data or AI layers.

**Device:** Samsung Galaxy S21 FE (SM-G990E), Exynos 2100, 8 cores, 8 GB RAM, Android 16. **Toolchain:** .NET 10.0.401, android workload 36.1.118 (Mono runtime; Release builds AOT, with LLVM after this spike), Avalonia 12.1.4, ONNX Runtime 1.31.1, Microsoft.ML.Tokenizers 2.0.1.

## Answer

**Yes, with no blocker.** Every building block runs on the phone, and the shared projects work there unchanged. Three things need work before E20 relies on them:

- **The vector search hot path** (D067) is 20 to 100 times slower on Mono than on desktop, because half-to-float widening isn't vectorised there. LLVM AOT and a bit-operation conversion bring 100k vectors to 110 ms per query; 300k is still over budget.
- **EF Core's first use** costs 1.5 to 1.9 s per process, and the compiled model that D071 suggested doesn't work yet.
- **ONNX Runtime's Android package** adds `INTERNET` and a telemetry hook. The head now strips both.

## Results

| # | Check | Result | Notes |
|---|---|---|---|
| 1 | Avalonia Android head builds, deploys and shows the shared main view | **Pass** | `src/MediaFileClassifier.App.Android`. The shell moved from `MainWindow` into a shared `MainView`, which Android shows as its single view. Navigation works through the shared `ViewLocator`. |
| 1a | Only `Desktop` uses Windows APIs | **Pass** | Shared code checks `OperatingSystem.IsWindows()` in three places (path comparison, the FFmpeg file name, lock error codes). The `dxgi.dll` and `explorer.exe` calls are in `Desktop` only. |
| 1b | Shared projects work on Android unchanged | **Pass** | The head references them as `net10.0`. They built, AOT-compiled and ran without changes, with no trimming or platform warnings. |
| 2 | `Microsoft.Data.Sqlite`: open, WAL, FTS5 | **Pass** | Through the real `ILibraryStore`: create, migrate, write, read, integrity check, `VACUUM INTO`, reopen. SQLite 3.53.3, `journal_mode=wal`, FTS5 with `unicode61 remove_diacritics 2` (Cyrillic, `cafe` finds `Café`) and `trigram`. |
| 2a | EF Core compiled model (D071) | **Fail, not needed yet** | Generating it needs two `DataAccess` changes. Once built, it spins the CPU on the phone and never completes the first query (details below). |
| 3 | sqlite-vec loads on arm64 | **Skipped** | D067 chose half-precision BLOBs searched in memory, so sqlite-vec isn't used. Its Android load is moot. |
| 3a | D067 in-memory search on the phone (deferred from MFC-17) | **Works; 300k over budget** | 100k vectors: 110 ms per query, 300k: 275 ms (LLVM AOT, 8 threads). The default AOT is about 4 times slower. Details below. |
| 4 | ONNX Runtime runs a small model | **Pass (provisional model)** | multilingual-e5-small and the SigLIP 2 base text tower, both int8. CPU is the fastest provider. XNNPACK crashes on e5. NNAPI is slower. |
| 5 | Storage Access Framework folder: enumerate, read streams | **Pass** | `IPlatformServices.PickFolderAsync` and an `IMediaFileSource` over `DocumentsContract`. A persisted tree URI is the `FolderHandle.Id`. |
| 6 | Measurements | **Done** | Below. |

## 1. Android head

- **Project:** `src/MediaFileClassifier.App.Android` targets `net10.0-android` (min API 26, arm64 only). It's the Android composition root: the same modules as `Desktop` (`AddInfrastructure`, `AddDataAccess`, `AddUseCases`, `AddUi`), with `AddAndroidPlatform` instead of `AddDesktopPlatform`.
- **Shared view:** `Ui/Views/MainView` now holds the shell, and `MainWindow` hosts it. `App` sets `ISingleViewApplicationLifetime.MainView` on Android. This is the only shared-code change the head needed.
- **Spike page:** the head registers `SpikeViewModel` in the shared `ViewLocator` and shows it as `CurrentPage`. It closes the side panels to fit a phone. Checks also run from adb: `adb shell am start -n com.raynir.mediafileclassifier/crc64c1b0e699992a1ec6.MainActivity --es spike "sqlite;onnx:e5:cpu"`, with results under the `MfcSpike` logcat tag.
- **Solution and CI:** the head stays in `MediaFileClassifier.slnx` with `<Build Project="false" />`, so a plain `dotnet build` and `dotnet test` don't need the android workload. A new PR-only job, **Build Android head** (ubuntu, since Linux minutes cost half), installs the workload and builds it. The Linux shared-projects loop skips it, like `Desktop`.
- **Namespace:** inside `namespace MediaFileClassifier.App.Android`, the identifiers `Android.*` and `App` resolve to our own namespaces. The head imports Android types with `using` directives and aliases (`UiApp`, `AndroidUri`). Fully qualified `Android.…` names don't compile there.
- **The shell is desktop-shaped:** a menu bar, a navigation rail and a details column. It renders and works, but a phone needs its own navigation (E20).

## 2. SQLite and EF Core

Each figure is from three Release runs on the phone, through the real `Infrastructure.DataAccess` code.

| Step | Time |
|---|---|
| First `ILibraryStore.CreateAsync` in a process (EF model, first queries, migration) | **1,480–1,860 ms** |
| Second `CreateAsync` in the same process | 73–149 ms |
| `OpenAsync` of an existing library | 22–34 ms |
| Write and read back a folder through the repositories | 197–254 ms (first write, includes query compilation) |
| `CheckIntegrityAsync` / `VACUUM INTO` backup (tiny database) | 2–3 ms / 6–8 ms |

- **WAL, foreign keys and the D077 lock file** work as on desktop. `PRAGMA journal_mode` reads back `wal`, and `schema_version` has the migration row.
- **The first EF use costs about 1.5 s per process.** That's model building plus first-query compilation under Mono AOT. Opening a library at startup should run off the UI thread, behind a splash or skeleton. The cost is per process, not per library.
- **The compiled model (D071) doesn't work yet.** `dotnet ef dbcontext optimize` fails until `DataAccess` changes in two ways:
  1. `TagKeyGenerator` is configured with `HasValueGenerator<T>()`, and the compiled model needs `HasValueGeneratorFactory<T>()`.
  2. The generated code calls the value converters' private `ToJson`, `FromJson` and `VectorComparer.AreEqual`, so they must be at least `internal`.

  With both changes made temporarily (not committed), the 131 desktop data-access tests pass in 11 s. On the phone, though, the first `CreateAsync` ran for 7 minutes at about 50% CPU (2 min 24 s of CPU time) and never finished. The cause isn't known: possibly the very large generated methods under Mono AOT. **Recommendation:** don't adopt the compiled model now (D083). Revisit it in E20 with a newer EF Core or runtime, together with trimming.
- **Trimming:** Release builds of the head trim with the Android defaults (SDK assemblies only). EF Core and our assemblies aren't trimmed, and the build reported no trim warnings. Full trimming isn't tested; with EF Core it would need the compiled model.

## 3. Vector search on the phone (D067)

MFC-17 deferred its Android numbers here. The check repeats its sizing case: 512-dim L2-normalised vectors stored as half-precision BLOBs in SQLite, loaded into one `Half[]` with a data reader, then scored brute force (`ConvertToSingle`, then `Dot`) with top-20 selection. The vectors are random, so this measures cost, not recall.

| Build | Vectors | Load from SQLite | Memory added (PSS) | `ConvertToSingle` + `Dot`, 1 thread | Same, 8 threads | Bit-operation widening, 8 threads |
|---|---|---|---|---|---|---|
| Default AOT | 100k | 0.9 s | +170 MB | 1,313–1,467 ms | 357–394 ms | 388 ms |
| Default AOT | 300k | 2.5–3.6 s | +295 MB | 4,397–4,491 ms | 972–973 ms | 1,141 ms |
| **LLVM AOT** | 100k | 0.6 s | +173 MB | 461 ms | 153 ms | **110 ms** |
| **LLVM AOT** | 300k | 1.7 s | +368 MB | 1,512 ms | 456 ms | **275 ms** |
| Desktop (MFC-17, for comparison) | 300k | ~2 s | ~300 MB | 45 ms | 6 ms | — |

Split of one single-threaded pass over 300k vectors, default AOT: `ConvertToSingle(Half)` 4,131 ms, `Dot` 134 ms.

- **The cost is the half-to-float conversion, not the dot product.** On Mono, `AdvSimd.IsSupported` is false, so `TensorPrimitives.ConvertToSingle(Half)` runs a software path: 4.1 s of a 4.4 s pass. `Dot` over the same data takes 134 ms, so `Vector128` itself is accelerated.
- **LLVM AOT helps a lot,** 2 to 3 times overall. Converting with integer bit operations on `Vector128<uint>` helps further under LLVM, with a maximum error of 3e-5 against the exact conversion, which is irrelevant for ranking. Under the default AOT it doesn't help.
- **Where that leaves the budget (200 ms, D031):** 100k vectors fit at 110 ms; 300k doesn't, at 275 ms. Float32 in memory would score 300k in about 30–40 ms, but needs 600 MB.
- **Load and memory match desktop:** 1 KB per vector in the managed heap, and about 2 s to load 300k from SQLite.
- **Decision (user, D084):** D067 stays as it is. Storage stays half-precision BLOBs. E20-S05 owns the Android search path: it starts from LLVM AOT and bit-operation widening, and adds int8 in memory or a smaller per-item budget if real libraries need it. The head's Release builds now use LLVM AOT (D081).

## 4. ONNX Runtime

**Provisional models** (MFC-53 hasn't picked one; the user chose to try both):

- **multilingual-e5-small, int8** (`Xenova/multilingual-e5-small`, 113 MB): XLM-R SentencePiece tokenizer, mean pooling, 384 dims. Text only, so it isn't a real candidate for image search.
- **SigLIP 2 base text tower, int8** (`onnx-community/siglip2-base-patch16-224-ONNX`, `text_model_int8.onnx`, 270 MB): Gemma SentencePiece tokenizer, pooled output, 768 dims. It has a matching image tower.

Both run through `Microsoft.ML.Tokenizers` (`SentencePieceTokenizer`, BPE and Unigram) and `Microsoft.ML.OnnxRuntime`. Each run is a fresh process: load the model, run 1 warm-up and then 40 inferences over 4 queries.

| Model | Provider | Load | First inference | Median | p90 | PSS before → after load |
|---|---|---|---|---|---|---|
| e5-small int8 | **CPU** | 490–512 ms | 40–54 ms | **4.4–5.2 ms** | 5.4–6.3 ms | 180 → 425 MB |
| e5-small int8 | XNNPACK | 377–490 ms | **crash** | — | — | 180 → 424 MB |
| e5-small int8 | NNAPI | 810 ms | 184 ms | 134 ms | 166 ms | 182 → 425 MB |
| SigLIP 2 text int8 | **CPU** | 1,095 ms | 117 ms | **49.5 ms** | 55.9 ms | 179 → 614 MB |
| SigLIP 2 text int8 | XNNPACK | 732 ms | 172 ms | 168 ms | 174 ms | 153 → 611 MB |
| SigLIP 2 text int8 | NNAPI | 992 ms | 251 ms | 316 ms | 453 ms | 206 → 618 MB |

- **The results are correct.** Token IDs match a desktop run of the same `TextEncoder` code exactly. The embeddings agree to about 1e-3, as expected from int8 kernels on ARM versus x86. Cosine similarities match within 0.003.
- **Both models are multilingual.** Cosine similarity of "a dog playing on the beach" with the Russian and German translations is 0.89/0.92 (e5) and 0.91/0.93 (SigLIP 2), against 0.70 and 0.74 for an unrelated sentence.
- **CPU is the right default on Android.**
  - NNAPI is 3 to 30 times slower, and it's deprecated since Android 15.
  - XNNPACK is 3.4 times slower on SigLIP 2. On e5 it kills the process during the first inference with no managed exception, reproduced three times. That points to a native crash on its int8 path.
  - `IHardwareInfo.PreferredExecutionProvider` should answer CPU on Android until a device-specific provider (QNN, or a GPU delegate) is measured.
- **Memory is the real cost.**
  - Loading e5 adds about 245 MB of PSS, and SigLIP 2 about 435 MB.
  - About 56–68 MB of that is managed heap that appears while the tokenizer loads. `SentencePieceTokenizer` holds the 250k/256k-entry vocabularies as managed objects.
  - With the D067 vectors (300 MB at 300k), a phone session would sit near 1 GB. That's fine on this 8 GB phone, but risky on 4 GB devices, where Android kills large background apps.
- **APK impact:** `libonnxruntime.so` is 12.3 MB compressed (32.6 MB installed), the largest single item in the APK.

### ONNX Runtime's Android package adds network access

The AAR's manifest merges `android.permission.INTERNET`, `ACCESS_NETWORK_STATE` and an `ai.onnxruntime.TelemetryInitializer` content provider into the app. The provider starts a 1DS telemetry HTTP client when the app launches. On this build it failed with a missing JNI method, but it was there. With the user's agreement, the head now:

- removes both permissions and the provider in `Properties/AndroidManifest.xml` with `tools:node="remove"`;
- calls `OrtEnv.Instance().DisableTelemetryEvents()` before the first session.

The merged manifests of both Debug and Release now have no network permission, and Debug builds still deploy and run. A future dependency could bring them back, so E20 should check the merged manifest in CI (risk R1).

## 5. Storage Access Framework

The picker was opened through `IPlatformServices.PickFolderAsync` (the Avalonia storage provider underneath), and the user picked a test folder (`MFC Test Data`).

- **Handle:** `content://com.android.externalstorage.documents/tree/primary%3AMFC%20Test%20Data`. The grant is persisted with `TakePersistableUriPermission`.
- **Enumerate:** 5 files in 64 ms. `SafMediaFileSource` runs one `DocumentsContract` query per folder; `DocumentFile` would run one per file and property.
- **Stat and read:** `GetStatAsync` by relative path took 10–13 ms with matching sizes. `OpenReadAsync` returned seekable streams, and SHA-256 hashing worked. The folder was small, so the throughput numbers (1–6 MB/s) aren't meaningful.

How MFC-57's abstractions map onto Android:

| Abstraction | Android | Gap |
|---|---|---|
| `FolderHandle.Id` | Persisted tree URI | None. It's opaque, as designed. |
| `FolderHandle.DisplayName` | Avalonia's `IStorageFolder.Name` | It returns the document ID (`primary:MFC Test Data`), not a name. Take `COLUMN_DISPLAY_NAME` instead. |
| `FileLocation.RelativePath` | Resolved segment by segment, one query per folder level | No lookup by path in SAF. Deep trees need a document-ID cache (or storing document IDs next to paths) for stat and open. |
| `EnumerateFilesAsync` / `OpenReadAsync` / `GetStatAsync` | `DocumentsContract` queries / `ContentResolver.OpenInputStream` | None for the smoke test. Throughput on a real camera folder is still to be measured (E20). |
| `PickFilesAsync` | Avalonia file picker | A picked file has no granted parent, so the file's URI stands in for the folder. |
| `CanRevealInFileExplorer` / `RevealInFileExplorerAsync` | `false` / `NotSupportedException` | As designed. |
| `OpenWithDefaultAppAsync` | Not implemented in the spike | Needs an `ACTION_VIEW` intent with a read grant (E20, D026). |
| `IAppPaths` | `AppPaths(Context.FilesDir)` | `CacheDirectory` is derived from `DataDirectory`. Android has its own `Context.CacheDir`, which the OS may clear, and thumbnails belong there. |

Android caps how many URI grants an app can persist (128 before Android 11, 512 since). That's plenty for library roots, but not for per-file grants.

## 6. Measurements

Release build, AOT, arm64, on the device above.

| What | Value |
|---|---|
| APK | **48.3 MB** with the default AOT, **49.9 MB** with LLVM AOT (2.6 MB of it is dex). Largest parts, compressed: `libonnxruntime.so` 12.3 MB, assembly store 8.3 MB, SkiaSharp 3.7 MB, EF Core AOT images 3.1 MB, SQLite 0.8 MB. |
| Cold start to first frame (`am start -W`, `TotalTime`) | **1.05–1.19 s** (default and LLVM AOT alike), and 2.2 s on the first launch after install |
| Process start → shell shown | 0.6–1.1 s (services built at ~0.2 s) |
| Idle memory with the shell shown (PSS) | **~200 MB** |
| Release build of the head (clean) | ~90 s with the default AOT, ~166 s with LLVM AOT |
| First library open (EF Core cold) | 1.5–1.9 s; warm open ~25 ms |
| e5-small int8, CPU | 0.4–0.5 s load, ~4–5 ms per query, +245 MB |
| SigLIP 2 text int8, CPU | 0.9–1.1 s load, ~41 ms per query with LLVM (~50 ms without), +435 MB |
| 300k × 512 half vectors (D067) | 1.7 s load, +300–370 MB, 275 ms per query (LLVM, 8 threads, bit-operation widening) |

## Risks and proposed backlog items

None of these is in Jira yet. They wait for the user's go-ahead.

| # | Risk | Impact | Proposed backlog item |
|---|---|---|---|
| R1 | A dependency's AAR adds `INTERNET` or telemetry (ONNX Runtime does today) | Breaks the offline promise without anyone noticing | **E20:** a CI check that the merged Android manifest has no network permission and no telemetry provider |
| R2 | D067's half-precision scoring is slow on Mono (no vectorised half conversion) | Search misses the 200 ms budget (D031) on phones at large sizes | **E20-S05:** an Android search path that meets D031: LLVM AOT and bit-operation widening, then int8 in memory or a per-item budget, measured on real libraries |
| R3 | EF Core costs 1.5–1.9 s on first use per process, and the compiled model hangs on Android | Slow first library open on the phone | **E20:** open the library off the UI thread at startup; investigate the compiled model under Mono AOT or a newer EF Core. Needs the two `DataAccess` changes listed above. |
| R4 | XNNPACK crashes on e5 int8, and NNAPI is slower than CPU | Wrong default provider means crashes or slow search | **MFC-53 / E06:** pick the execution provider per platform. Android defaults to CPU, and `IHardwareInfo.PreferredExecutionProvider` returns it. Re-test providers with the chosen model. |
| R5 | Model memory: +245 to +435 MB, plus ~60 MB managed for the tokenizer | Out-of-memory kills on 4 GB phones next to the vector index | **MFC-53:** weigh memory on Android (fp16 or smaller towers, a leaner tokenizer). **E20:** unload the model when idle, and set a memory budget. |
| R6 | How models reach the phone without `INTERNET` | No download path that keeps the offline promise | **New decision:** how models get onto the phone (APK or Play asset pack, user import through SAF, or `DownloadManager`) |
| R7 | SAF has no lookup by path; the display name comes out wrong; cache folder mapping | Slow stat/open on deep trees; wrong names in the UI | **E20:** an SAF media source with a document-ID cache, display names from `COLUMN_DISPLAY_NAME`, and `IAppPaths.CacheDirectory` mapped to `Context.CacheDir` |
| R8 | The desktop shell (menu bar, three columns) on a phone | Not usable as a phone UI | **E20:** phone navigation and layout for `MainView` |
| R9 | Android workloads break after SDK updates (they did on the dev machine, see below) | Lost time for every developer | **E01:** document Android setup in `docs/development-workflow.md`: `dotnet workload install android` (elevated), and the symptom (a misleading `wasm-tools` NETSDK1147) |
| R10 | The head logs to a null logger, so there are no file logs on Android | No diagnostics from phones | **E20:** Serilog to the app's files folder, plus logcat |

**Workload note (R9):** on the dev machine, the 10.0.114 SDK update installed a mono toolchain manifest that needed 10.0.14 packs, which weren't installed. Every Android project then failed restore with `NETSDK1147: ... wasm-tools`, even a plain `dotnet new android`. An elevated `dotnet workload update` followed by `dotnet workload install android` fixed it.

## Drafted decisions

Numbered from D080 for the coordinator; the user accepted each of them during the spike.

> **D080 — Android head.** The Android app is `src/MediaFileClassifier.App.Android` (`net10.0-android`, min API 26, arm64), the Android composition root over the shared projects. The shell is a shared `MainView` UserControl: `MainWindow` hosts it on desktop, and `ISingleViewApplicationLifetime` shows it on Android. The head stays in the solution with `Build Project="false"`, so a default `dotnet build` needs no android workload. A PR-only CI job installs the workload and builds the head. *(Source: MFC-18.)*

> **D081 — No network on Android, and LLVM AOT.** The Android app declares no network permission. Dependencies that add one, or telemetry, are stripped in the head's manifest with `tools:node="remove"`; ONNX Runtime's `INTERNET`, `ACCESS_NETWORK_STATE` and `TelemetryInitializer` are, and `OrtEnv.DisableTelemetryEvents()` is called. Release builds use LLVM AOT (`EnableLLVM`): 2–3 times faster managed hot loops, for +1.6 MB of APK and about 75 s more build time. *(Source: MFC-18.)*

> **D082 — Provisional application ID.** `com.raynir.mediafileclassifier`, to be confirmed before E20 publishes anything, because it can't change after a store release. *(Source: MFC-18.)*

> **D083 — Android runtime choices (provisional).** ONNX Runtime uses the **CPU** execution provider on Android. On the test phone, NNAPI was 3–30 times slower, and XNNPACK crashed (e5 int8) or was 3.4 times slower (SigLIP 2). Re-test once MFC-53 picks the model, including QNN or GPU. EF Core runs **without a compiled model** for now: generating one needs two `DataAccess` changes, and it hung on the phone. The library opens off the UI thread, because EF's first use costs 1.5–1.9 s. Revisit with trimming in E20. *(Source: MFC-18; amends the compiled-model note in D071.)*

> **D084 — D067 on Android.** D067 stands: half-precision BLOBs searched in memory. On Android, Mono doesn't vectorise half-to-float conversion. With LLVM AOT and bit-operation widening, a query takes 110 ms at 100k vectors and 275 ms at 300k (8 threads). E20-S05 owns meeting D031 on phones, with int8 in memory or a per-item budget as the next steps. *(Source: MFC-18.)*
