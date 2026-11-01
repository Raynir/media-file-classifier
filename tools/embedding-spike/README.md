# Embedding model spike tools (MFC-53, E06-SP01)

These Python scripts embedded the evaluation corpus with each candidate model, exported and timed the ONNX towers, and checked the tokenizers in .NET. The findings are in [docs/spikes/embedding-model.md](../../docs/spikes/embedding-model.md). This is spike code, not part of the app, so nothing here is referenced by the solution.

## Setup

Everything large stays in the git-ignored `models/` folder at the repository root: the virtual environment, the Hugging Face cache, ONNX exports, frame-vector caches, results and benchmarks. Model weights take about 15 GB.

```powershell
pwsh tools/eval-corpus/fetch-corpus.ps1
py -3.14 -m venv models/.venv
models/.venv/Scripts/python -m pip install torch==2.11.0 torchvision==0.26.0 --index-url https://download.pytorch.org/whl/cu128
models/.venv/Scripts/python -m pip install -r tools/embedding-spike/requirements.txt
$env:HF_HOME = "$PWD/models/hf"
```

ONNX Runtime 1.31's GPU package needs the CUDA 13 runtime; the `[cuda,cudnn]` extra installs it from pip, and `onnxruntime.preload_dlls()` finds it. PyTorch keeps its own CUDA 12 libraries.

## Scripts

Run from the repository root with `models/.venv/Scripts/python`. Run them **one at a time**: the larger models fill an 8 GB GPU, and Windows then spills GPU memory into system RAM.

| Script | What it does |
|---|---|
| `run_retrieval.py <model> [--lowercase] [--onnx fp32:int8] [--batch N]` | Embeds the corpus (8 even frames per GIF or video, mean of the frame vectors) and writes `models/results/<system>.json` for the runner, plus a `-maxframe` variant. `--onnx` runs the exported towers on the CPU instead of the reference model. |
| `export_onnx.py <model>` | Writes `models/onnx/<model>/{image,text}.onnx` and int8 (dynamic) copies. |
| `bench_onnx.py <model> [--batch N]` | File sizes, image throughput (CPU 8 threads, all cores, CUDA) and single-query text latency. Writes `models/bench/<model>.json`. |
| `compare_text_towers.py <model> [--lowercase]` | Scores int8, per-channel int8, MatMul-only int8 and fp16 text towers against the same image vectors. |
| `export_token_cases.py`, then `dotnet run tools/embedding-spike/TokenizerCheck.cs` | Checks that `Microsoft.ML.Tokenizers` gives the Python token IDs for all evaluation queries. |
| `summarize.py <system>...` | Prints the findings doc's tables from the runner reports and benchmarks. |

Score a results file with the runner, which writes the report to `eval/reports/`:

```powershell
dotnet run --project tools/MediaFileClassifier.Evaluation -- evaluate --results models/results/siglip2-base-patch16-256-lowercase.json
```

The model names are the keys of `ENCODERS` in `encoders.py`. SigLIP 2 needs `--lowercase`, because it was trained on lower-cased text.
