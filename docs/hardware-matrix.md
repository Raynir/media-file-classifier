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
| NVIDIA discrete GPU + integrated GPU (hybrid laptop) | Dev machine: RTX 4070 Laptop (adapter 0), Intel UHD (adapter 1), 28 logical processors, 31.7 GB | DirectML on the NVIDIA adapter | **Checked**: adapter choice picks the discrete GPU; DirectML 1.24.4 lists `DmlExecutionProvider` and runs a small model on both adapters (T02 smoke test); fallback paths covered by unit tests | **Open**: the SigLIP 2 files aren't on this machine |
| Integrated GPU only (Intel/AMD APU, under 2 GB dedicated) | Intel UHD alone | CPU (by design: the 2 GB threshold of DNEW-3) | **Open** (the rule is unit-tested; the dev machine's iGPU is never the preferred adapter) | **Open** |
| AMD discrete GPU | Radeon RX series | DirectML | **Open** | **Open** |
| Intel Arc discrete GPU | Arc A-series | DirectML | **Open** | **Open** |
| No GPU / driver without DirectX 12 | Old desktop, a VM | CPU | **Open** on a real machine; the CPU path is the default of every unit test | **Open** |
| NVIDIA with an outdated or broken driver | | DirectML fails to load or on its first run, then CPU | **Open** on a real machine; simulated in `ExecutionProviderFallbackTests` (missing provider, failed load, failed first run) | **Open** |
| ARM64 Windows | Snapdragon X | CPU (the DirectML package is x64 only; the app is AnyCPU) | **Open** | **Open** |

## Known gaps

- **No real-model numbers yet.** The SigLIP 2 files aren't installed on the only machine used so far. Once installed, run the benchmark on each row and paste its table in a "Results" section below. The quality gate (`MFC_SIGLIP2_DIR` real-model tests) is described in the MFC-85 contract.
- **Machines.** AMD, Intel Arc, a no-GPU machine and ARM64 are not available to the developer. Ask testers for the benchmark output and the first log lines from those.
- **DirectML on the integrated GPU** is never chosen when a discrete one exists, and is skipped below 2 GB. Whether a UHD/Iris GPU would beat the CPU is unmeasured; the benchmark only tries the preferred adapter. The CUDA build for NVIDIA users is a backlog item, not part of this matrix.
- **Android** (NNAPI) isn't built or tested here.

## Results

None recorded yet.
