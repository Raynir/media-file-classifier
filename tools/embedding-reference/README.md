# SigLIP 2 reference outputs (MFC-85, E06-S03)

`make_reference.py` writes the outputs that the .NET embedding tests compare against: image vectors for the sample media, text vectors for 10 evaluation queries, and the token IDs of all 215 queries. The reference is the Hugging Face processor (Pillow resize, Gemma tokenizer) feeding the same ONNX towers the app runs. The model spike showed that these towers give the PyTorch model's vectors ([docs/spikes/embedding-model.md](../../docs/spikes/embedding-model.md)), so no PyTorch is needed.

## Model files

The tests that run the model look for the files in `models/siglip2` at the repository root (git-ignored), or in the folder `MFC_SIGLIP2_DIR` names. Without them they skip, which is what happens in CI.

| File | From |
|---|---|
| `vision_model.onnx` (372 MB) | `onnx-community/siglip2-base-patch16-256-ONNX`, `onnx/vision_model.onnx` |
| `text_model.onnx` (1,129 MB, fp32, D093) | the same repository, `onnx/text_model.onnx` |
| `tokenizer.model` (4 MB) | `google/siglip2-base-patch16-256` |

```powershell
$base = "https://huggingface.co/onnx-community/siglip2-base-patch16-256-ONNX/resolve/main"
New-Item -ItemType Directory -Force models/siglip2
curl.exe -L -o models/siglip2/vision_model.onnx "$base/onnx/vision_model.onnx"
curl.exe -L -o models/siglip2/text_model.onnx "$base/onnx/text_model.onnx"
curl.exe -L -o models/siglip2/tokenizer.model "https://huggingface.co/google/siglip2-base-patch16-256/resolve/main/tokenizer.model"
```

The app itself finds the model in its model store (`%LOCALAPPDATA%\MediaFileClassifier\models\siglip2-base-patch16-256\1`), with the roles `vision`, `text` and `tokenizer`. The model download (E06-S12) installs it there.

## Regenerating the reference

Only needed when the model files or the reference preprocessing change.

```powershell
py -3.14 -m venv models/.venv
models/.venv/Scripts/python -m pip install onnxruntime==1.31.1 transformers==5.22.1 pillow==13.0.0 sentencepiece==0.2.2 numpy==2.5.4 protobuf==7.36.2 tokenizers==0.23.3
$env:HF_HOME = "$PWD/models/hf"
models/.venv/Scripts/python -I tools/embedding-reference/make_reference.py models/siglip2
```

It writes `tests/MediaFileClassifier.Infrastructure.Tests/AI/Embeddings/siglip2-reference.json`. For PNG and GIF samples it also stores the SHA-256 of Pillow's resized pixels, which the .NET resampler must match byte for byte; that test runs in CI.
