# Hardware test matrix (E06-S08-T05)

Which machines the hardware acceleration (execution provider choice, fallback, model tiers) has been checked on, what was checked, and what is still open. Update a row whenever a machine is tested.

How the app chooses (see [native-dependencies.md](native-dependencies.md)): the adapter with the most dedicated memory (at least 2 GB) gets DirectML, otherwise the CPU runs. A provider that is missing, fails to load or fails its first run falls back to the CPU, and the reason is logged. The installed model tier that suits the provider is used: `standard` (fp32) on the CPU, `compact` (fp16 text tower) on a GPU.

## Run the checks

```powershell
dotnet run --project src/MediaFileClassifier.Desktop -- --benchmark
```

It times SigLIP 2's image and text towers on 24 generated 640×480 images and 64 texts, for every installed tier, on the CPU and on the preferred GPU. It prints a table and saves it to `%LOCALAPPDATA%\MediaFileClassifier\benchmark.json`, which also seeds the estimated time remaining of an analysis run. It needs the model installed. It takes about 5 s per tier on the CPU, so under two minutes in total. The first log lines of every run (`%LOCALAPPDATA%\MediaFileClassifier\logs`) name the detected hardware and the chosen provider, and each session logs `Loaded … on <provider>` or why it fell back.

## Matrix

Legend: **Checked** = done on a real machine and passed. **Open** = not done.

| Machine class | Example | Provider expected | Provider and fallback checks | Benchmark (real model) |
|---|---|---|---|---|
| NVIDIA discrete GPU + integrated GPU (hybrid laptop) | Dev machine: RTX 4070 Laptop (adapter 0), Intel UHD (adapter 1), 28 logical processors, 31.7 GB | DirectML on the NVIDIA adapter | **Checked**: adapter choice picks the discrete GPU; DirectML 1.24.4 lists `DmlExecutionProvider` and runs a small model on both adapters (T02 smoke test); fallback paths covered by unit tests | **Checked** (2026-11-11), see Results |
| Integrated GPU only (Intel/AMD APU, under 2 GB dedicated) | Intel UHD alone | CPU (by design: the 2 GB threshold of DNEW-3) | **Open** (the rule is unit-tested; the dev machine's iGPU is never the preferred adapter) | **Open** |
| AMD discrete GPU | Radeon RX series | DirectML | **Open** | **Open** |
| Intel Arc discrete GPU | Arc A-series | DirectML | **Open** | **Open** |
| No GPU / driver without DirectX 12 | Old desktop, a VM | CPU | **Open** on a real machine; the CPU path is the default of every unit test | **Open** |
| NVIDIA with an outdated or broken driver | | DirectML fails to load or on its first run, then CPU | **Open** on a real machine; simulated in `ExecutionProviderFallbackTests` (missing provider, failed load, failed first run) | **Open** |
| ARM64 Windows | Snapdragon X | CPU (the DirectML package is x64 only; the app is AnyCPU) | **Open** | **Open** |

## Known gaps

- **One machine only.** Run the benchmark on each other row and paste its table in the Results section below. The quality gate (`MFC_SIGLIP2_DIR` real-model tests) is described in the MFC-85 contract.
- **Machines.** AMD, Intel Arc, a no-GPU machine and ARM64 are not available to the developer. Ask testers for the benchmark output and the first log lines from those.
- **DirectML on the integrated GPU** is never chosen when a discrete one exists, and is skipped below 2 GB. Whether a UHD/Iris GPU would beat the CPU is unmeasured; the benchmark only tries the preferred adapter. The CUDA build for NVIDIA users is a backlog item, not part of this matrix.
- **Android** (NNAPI) isn't built or tested here.

## Results

Dev machine (28 logical processors, 31.7 GB, RTX 4070 Laptop + Intel UHD), both tiers installed, 2026-11-11. The whole run took about 30 s. Generated 640×480 images, so these are throughput figures, not quality figures.

| Analyzer | Tier | Provider | Items/s |
|---|---|---|---|
| siglip2.image | standard | CPU | 8.7 |
| siglip2.text | standard | CPU | 34.2 |
| siglip2.image | standard | DirectML (RTX 4070) | 29.0 |
| siglip2.text | standard | DirectML (RTX 4070) | 185.4 |
| siglip2.image | compact | DirectML (RTX 4070) | 36.5 |
| siglip2.text | compact | DirectML (RTX 4070) | 251.2 |
| any | compact | CPU | does not load |

What it shows:
- The GPU embeds images about 3.3 to 4.2 times faster than the CPU and texts 5 to 7 times faster. The `compact` tier shares the `standard` image tower, so its image figure only differs by run-to-run noise (these runs ranged from 27.7 to 36.5 images/s).
- **The `compact` tier's fp16 text model doesn't load on the CPU** (ONNX Runtime fails to initialise it: `InsertedPrecisionFreeCast_…` in `SimplifiedLayerNormFusion`). So `compact` is a GPU-only tier. If DirectML falls back to the CPU while `compact` is the installed tier, the text tower is unusable until `standard` is installed. The provider handles it: a GPU tier that fails to load makes it use the installed CPU tier (`standard`) from then on, until the app restarts.
- The CPU figure here (8.7 images/s) is below MFC-85's 14.7 images/s, which was measured differently (see its contract); compare runs only with the same command.

## Full analysis profile (E06-S04-T06)

The full profile without the describer (D368 amends D031): SigLIP 2 image embedding (`standard` tier) and PP-OCRv5 text recognition on each picture, one item after the other with a batch of one and no thumbnails or library writes, so it's a conservative per-item rate rather than the app's pipelined one. Opt-in test `FullProfileThroughputTests` (`MFC_THROUGHPUT_SAMPLES`, `MFC_OCR_DIR`, SigLIP 2 as for the other real-model tests; build with `-p:OnnxGpu=true` for the DirectML runtime). 50 describer-spike samples (up to 1024 px), dev machine (i7-14700HX, RTX 4070 Laptop with LM Studio's model loaded alongside), 2026-11-18:

| Tier | Provider | Items/s | Embedding ms/item | OCR ms/item |
|---|---|---|---|---|
| GPU | DirectML (RTX 4070 Laptop) | 4.75 | 50 | 161 |
| CPU | CPU (28 logical processors) | 0.81 | 628 | 609 |

The describer is measured on its own (D368): qwen3-vl-4b in LM Studio 7.76 s/image median on the GPU (budget 10 s, `OpenAiCompatibleImageDescriberLiveTests`), Florence-2 base 5.55 s/image on the CPU (`Florence2DescriberTests.Speed_OverTheSpikeInputs`).

- **GPU tier:** 4.75 items/s meets D368's ≥ 1 image/s for the profile without the describer.
- **CPU tier budget** (user's choice, drafted for D031): the profile without the describer ≥ 0.5 items/s, and the describer ≤ 8 s/image. This machine has more cores than D031's CPU tier (4–8), so its 0.81 items/s overstates the tier; a 4–8 core laptop is expected around 0.3–0.5 items/s and has not been measured.
- Embedding one picture at a time on the CPU (628 ms) is much slower than the batched figure above (8.7 images/s); the app embeds in batches of 4.
