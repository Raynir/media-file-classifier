# Spike: multilingual embedding model selection (MFC-53, E06-SP01)

**Unblocks:** E06-S03 Semantic embeddings (MFC-85), E06-S12 Model catalogue (MFC-88). **Decisions:** D092 to D094, accepted by the user; drafts at the end. **Status:** measured on desktop. Android numbers for the chosen text tower are still to be measured on the phone.

## Question

Which image-text embedding model should power semantic search, given these requirements?

- English and Russian (D014, D029),
- every hardware class, including CPU only (D015),
- a text encoder small enough to run on Android (D004).

## Answer

**SigLIP 2 base-patch16-256** (`google/siglip2-base-patch16-256`, Apache-2.0, 768 dimensions), with lower-cased queries. On the evaluation corpus it scores recall@10 **0.960** in English and **0.815** in Russian.

- **It leads English by 6 points** over every multilingual alternative: 1.00 on text in images and 1.00 on look-alike distractors. In Russian the top five models are within 4 points of each other.
- **It's fast enough on every tier.** Images: 11.8/s on 8 CPU threads (budget 2/s) and 130/s on the GPU (budget 10/s). Queries: 26–32 ms on the CPU (budget 50 ms) and 3.5 ms on the GPU.
- **It runs in .NET today.** The publisher's ONNX export works as is, and `Microsoft.ML.Tokenizers` reproduces the Python token IDs for all 215 queries.

Two things matter as much as the model choice:

- **Queries must be lower-cased.** SigLIP 2 was trained on lower-cased text. Without it, English recall drops from 0.960 to 0.868.
- **The text tower must not be int8.** Every int8 variant, including the one MFC-18 ran on the phone, drops Russian recall to about 0.55 while English stays at 0.96. The fp16 tower is lossless and half the size, so Android ships fp16 (D093).

## Candidates

The licences and sizes were checked on Hugging Face on 2026-11-01. The user chose to run all of them (jina-clip-v2 was dropped later, see below).

| Model | Hugging Face | Licence (weights) | Dim | Notes |
|---|---|---|--:|---|
| SigLIP 2 base, 224/256/384 px | `google/siglip2-base-patch16-*` | Apache-2.0 | 768 | Gemma tokenizer (256k vocabulary). The three resolutions are separately trained models of the same size. |
| SigLIP 2 so400m, 256 px | `google/siglip2-so400m-patch16-256` | Apache-2.0 | 1152 | The quality ceiling of the family. |
| MetaCLIP 2 worldwide, ViT-S/16 | `facebook/metaclip-2-worldwide-s16` | **CC-BY-NC-4.0** | 384 | XLM-V tokenizer (900k vocabulary). |
| MetaCLIP 2 mT5, ViT-S/16 | `facebook/metaclip-2-mt5-worldwide-s16` | **CC-BY-NC-4.0** | 384 | mT5 tokenizer, smallest multilingual text tower. |
| OpenCLIP XLM-R base, ViT-B/32 | `laion/CLIP-ViT-B-32-xlm-roberta-base-laion5B-s13B-b90k` | MIT | 512 | |
| Multilingual CLIP (distilled) | `sentence-transformers/clip-ViT-B-32-multilingual-v1` + OpenAI ViT-B/32 | Apache-2.0 / MIT | 512 | DistilmBERT student of the OpenAI text encoder, in the M-CLIP style. |
| MEXMA-SigLIP 2 | `visheratin/mexma-siglip2` | MIT | 1152 | MEXMA (XLM-R large) text tower with the SigLIP 2 so400m 512 px image tower. |
| jina-clip-v2 | `jinaai/jina-clip-v2` | **CC-BY-NC-4.0** | 1024 | **Dropped, not measured.** At batch 16 its 3.3 GB of fp32 weights overflowed the 8 GB GPU, and Windows spilled GPU memory into system RAM until the machine ran out. Even at batch 2 it used nearly all 8 GB. The user dropped it: it doesn't fit the D031 GPU tier comfortably, and it's non-commercial. |
| Baseline: OpenAI CLIP ViT-B/16 | `openai` weights via OpenCLIP | MIT | 512 | English only. |

Not shortlisted: M-CLIP XLM-R-Large (same idea as OpenCLIP XLM-R but older, with a 560M-parameter text tower), NLLB-SigLIP (non-commercial and superseded by MEXMA-SigLIP 2), and ruCLIP (Russian only).

## How it was measured

The scripts are in [`tools/embedding-spike`](../../tools/embedding-spike/README.md). They write results files, and the E01-S10 runner scores them, so every number below is in a committed report in [`eval/reports`](../../eval/reports).

- **Corpus:** the E01-S10 public set: 351 items, with 105 English and 110 Russian queries.
- **Frames:** a photo, screenshot or document is one frame. A GIF or video gives 8 frames at even intervals, and the item vector is the mean of the normalised frame vectors, as E06-S02 specifies. A second run ranks each item by its best frame instead (`-maxframe`).
- **Ranking:** cosine similarity of the query vector with every item vector, top 100.
- **Reference implementations:** quality runs use PyTorch (transformers or OpenCLIP) in fp32. mexma-siglip2 only ships with custom code, so it was run from the publisher's ONNX export.
- **Speed:** measured on the ONNX towers the app would ship, exported with the TorchScript exporter (opset 17), on ONNX Runtime 1.31.1, the version the app uses.
  - Image throughput is the model call alone on a batch of 16 preprocessed corpus photos (8 for so400m, 1 for mexma), without decoding and resizing.
  - Text latency is one query per call, the median of 60 calls over an English and a Russian query.
- **Machine:** Intel Core i7-14700HX (20 cores, 28 threads), 32 GB RAM, NVIDIA RTX 4070 Laptop GPU (8 GB), Windows 11. This is faster than the D031 CPU tier (4 to 8 cores), so the 8-thread CPU rows are the guide; expect about half that speed on the tier. The RTX 4070 Laptop is close to the D031 GPU tier (RTX 3060 class).

## Results

### Retrieval quality

SigLIP models use lower-cased queries (see [Lower case](#lower-case-matters-for-siglip-2)). Lower-casing changes nothing for the other models, so they use the queries as written.

| Model | R@10 en | MRR en | R@10 ru | MRR ru |
|---|--:|--:|--:|--:|
| **SigLIP 2 base-256** | **0.960** | **0.984** | **0.815** | **0.837** |
| SigLIP 2 base-224 | 0.966 | 0.979 | 0.805 | 0.834 |
| SigLIP 2 base-384 | 0.968 | 0.983 | 0.843 | 0.819 |
| SigLIP 2 so400m-256 | 0.968 | 0.982 | 0.796 | 0.826 |
| MetaCLIP 2 worldwide-s16 | 0.893 | 0.959 | 0.839 | 0.835 |
| MetaCLIP 2 mT5-s16 | 0.900 | 0.942 | 0.805 | 0.837 |
| OpenCLIP XLM-R base B/32 | 0.903 | 0.924 | 0.833 | 0.880 |
| MEXMA-SigLIP 2 (publisher's int8 ONNX) | 0.852 | 0.847 | 0.755 | 0.741 |
| Multilingual CLIP (distilled) B/32 | 0.835 | 0.842 | 0.556 | 0.517 |
| OpenAI CLIP B/16 (English baseline) | 0.871 | 0.918 | 0.131 | 0.127 |

Recall@10 by query category, English / Russian:

| Model | object | scene | synonym | text | abstract | distractor | media |
|---|--:|--:|--:|--:|--:|--:|--:|
| **SigLIP 2 base-256** | 0.98 / 0.91 | 0.96 / 0.82 | 0.96 / 0.77 | 1.00 / 0.82 | 0.76 / 0.34 | 1.00 / 1.00 | 0.92 / 0.67 |
| SigLIP 2 base-224 | 0.99 / 0.92 | 0.97 / 0.81 | 0.96 / 0.77 | 1.00 / 0.78 | 0.81 / 0.31 | 1.00 / 1.00 | 0.92 / 0.70 |
| SigLIP 2 base-384 | 0.98 / 0.93 | 0.97 / 0.84 | 0.96 / 0.87 | 1.00 / 0.87 | 0.78 / 0.36 | 1.00 / 1.00 | 0.98 / 0.67 |
| SigLIP 2 so400m-256 | 1.00 / 0.81 | 0.98 / 0.78 | 0.98 / 0.60 | 1.00 / 0.93 | 0.75 / 0.31 | 0.99 / 0.99 | 0.92 / 0.73 |
| MetaCLIP 2 worldwide-s16 | 0.94 / 0.88 | 0.89 / 0.89 | 0.90 / 0.85 | 0.92 / 0.85 | 0.62 / 0.38 | 0.98 / 0.99 | 0.81 / 0.78 |
| MetaCLIP 2 mT5-s16 | 0.94 / 0.89 | 0.91 / 0.84 | 0.90 / 0.86 | 0.88 / 0.80 | 0.69 / 0.27 | 0.99 / 0.96 | 0.85 / 0.69 |
| OpenCLIP XLM-R base B/32 | 0.97 / 0.91 | 0.90 / 0.88 | 0.91 / 0.78 | 0.86 / 0.84 | 0.69 / 0.39 | 0.98 / 0.91 | 0.83 / 0.75 |
| MEXMA-SigLIP 2 | 0.91 / 0.90 | 0.83 / 0.85 | 0.77 / 0.72 | 0.93 / 0.64 | 0.50 / 0.38 | 0.95 / 0.67 | 0.81 / 0.81 |
| Multilingual CLIP B/32 | 0.90 / 0.60 | 0.81 / 0.63 | 0.79 / 0.28 | 0.81 / 0.54 | 0.59 / 0.49 | 0.99 / 0.52 | 0.80 / 0.54 |
| OpenAI CLIP B/16 | 0.95 / 0.03 | 0.92 / 0.03 | 0.88 / 0.18 | 0.82 / 0.40 | 0.56 / 0.04 | 1.00 / 0.09 | 0.67 / 0.08 |

- **English:** the SigLIP 2 family is clearly ahead, at 0.96–0.97 against 0.89–0.90 for the next models. The gap comes from scenes, text in images and distractors.
- **Russian:** the top models are within 0.80–0.84. With 110 queries, a difference of 0.03 is about three queries, so base-384's lead over base-256 isn't conclusive.
- **Every model is weak on Russian abstract queries** (mood or idea: 0.27–0.49) and somewhat weak on Russian `media` queries (the kind of file). Hybrid search with keywords and tags (E09) will have to carry these.
- **Bigger isn't better here.** so400m matches base in English and is worse in Russian, at four times the compute.
- **MEXMA-SigLIP 2 is understated.** Its publisher only ships ONNX towers that are already int8-quantized (dynamic `MatMulInteger`), with a fixed batch of 1 and a fixed text length of 32. Given what int8 does to SigLIP 2 below, its fp32 quality is probably higher, but measuring it needs the custom model code.
- **Videos and GIFs:** the mean of frame vectors (E06-S02) and the best frame score the same in English (0.960 both) and the mean is slightly better in Russian (0.815 against 0.807). D067's "an item scores as its best vector" and E06-S02's "the item embedding is the mean" both work; the item-level mean is what the evaluation used.

### Speed and size

ONNX Runtime 1.31.1. CPU columns are 8 threads; all-cores numbers are in `models/bench` and are 10–30% higher.

| Model | Input | Image tower fp32 / int8 | Text tower fp32 / int8 | Images/s CPU fp32 / int8 | Images/s CUDA | Query ms CPU fp32 / int8 | Query ms CUDA |
|---|---|--:|--:|--:|--:|--:|--:|
| **SigLIP 2 base-256** | 256 px | 355 / 94 MB | 1,077 / 270 MB | **11.8** / 21.9 | **130** | **25.7** / 9.1 | **3.5** |
| SigLIP 2 base-224 | 224 px | 355 / 94 MB | 1,077 / 270 MB | 15.1 / 28.6 | 181 | 25.3 / 10.2 | 3.6 |
| SigLIP 2 base-384 | 384 px | 356 / 95 MB | 1,077 / 270 MB | 4.7 / 8.8 | 52 | 25.9 / 10.5 | 3.4 |
| SigLIP 2 so400m-256 | 256 px | 1,633 / 412 MB | 2,700 / 678 MB | 2.5 / 5.9 | 39 | 99.7 / 42.5 | 12.3 |
| MetaCLIP 2 worldwide-s16 | 224 px | 83 / 21 MB | 1,403 / 351 MB | 68.2 / 113.9 | 773 | 3.7 / 1.4 | 3.1 |
| MetaCLIP 2 mT5-s16 | 224 px | 83 / 21 MB | 448 / 113 MB | 65.7 / 120.7 | 773 | 3.1 / 1.6 | 3.4 |
| OpenCLIP XLM-R base B/32 | 224 px | 336 / 85 MB | 1,062 / 266 MB | 60.4 / 101.9 | 830 | 30.3 / 10.9 | 5.0 |
| MEXMA-SigLIP 2 (int8 only, batch 1) | 512 px | 427 MB | 559 MB | 1.1 | 1.6 (runs on the CPU) | 22.8 | 46.9 |
| Multilingual CLIP B/32 | 224 px | 336 / 85 MB | 516 / 129 MB | 62.4 / 86.3 | 843 | 4.8 / 1.5 | 2.8 |
| OpenAI CLIP B/16 | 224 px | 329 / 84 MB | 243 / 62 MB | 15.1 / 29.3 | 258 | 15.1 / 9.7 | 3.2 |

- **SigLIP 2 base-256 meets the D031 embedding budgets with room to spare.** At half this CPU's speed, about 6 images/s on the CPU tier against a budget of 2. On the GPU, 130 images/s against 10. Decoding and resizing come on top, and E06-S08 measures them in .NET.
- **base-384 would be tight on the CPU tier.** At about 2–2.5 images/s it sits at the budget before decoding.
- **The SigLIP 2 text tower is large but fast.** About 75% of its 1,077 MB is the 256k-token embedding table (786 MB), which costs memory but no compute. A query takes 26–32 ms on 8 threads and 110 ms on one thread. That's within the 50 ms budget on 8 threads, but the CPU tier will be close to it; the query cache of E06-S03-T02 helps. The fp16 tower is no faster on x86 (36 ms).
- **The MetaCLIP 2 and B/32 models are 5 times faster on images.** Their smaller image towers (ViT-S/16, ViT-B/32) pay for that in English quality.
- **Embedding dimension:** 768 dimensions in half precision take 1.5 KB per vector, against 1 KB at 512. At D067's sizing case (100k items × 3 vectors) that's about 460 MB instead of 300 MB in memory. Desktop search time grows by the same factor, from 6 to about 9 ms in parallel. On Android (D084) it pushes 100k vectors from 110 to about 165 ms, still within 200 ms, and 300k further over budget, at about 410 ms. E20-S05 already owns that.

### Lower case matters for SigLIP 2

SigLIP 2 was trained on lower-cased text, and the Hugging Face processor doesn't lower-case. As written, the queries give English 0.868 and Russian 0.789. Lower-cased, they give 0.960 and 0.815. The loss is in scenes, text in images and distractors, where queries name proper nouns or signs ("Aurora", "METRO"). MFC-18's Android `TextEncoder` already lower-cases. The other models are unaffected, because their tokenizers lower-case themselves or they were trained on cased text.

### Quantization

The int8 text tower breaks Russian. All rows below use the same fp32 image vectors and change only the text tower of SigLIP 2 base-256:

| Text tower | Size | R@10 en | R@10 ru |
|---|--:|--:|--:|
| fp32 (exported here; identical to PyTorch) | 1,077 MB | 0.960 | 0.815 |
| **fp16** (weights only, fp32 inputs and outputs) | **539 MB** | **0.960** | **0.815** |
| q4f16 (publisher's `text_model_q4f16.onnx`) | 422 MB | 0.957 | 0.803 |
| int8 dynamic, per tensor | 270 MB | 0.958 | 0.550 |
| int8 dynamic, per channel | 270 MB | 0.958 | 0.544 |
| int8 dynamic, MatMul only (embedding table kept fp32) | 833 MB | 0.955 | 0.540 |
| int8, publisher's `text_model_int8.onnx` (the file MFC-18 measured) | 270 MB | 0.955 | 0.549 |

- **Quantizing the MatMuls is what breaks it,** not the embedding table: per-channel weights don't help, and keeping the table in fp32 doesn't either. Dynamic activation quantization seems to lose what separates Russian phrases, while English survives. `compare_text_towers.py` reproduces the table.
- **The int8 image tower costs another 10 points in English** (0.840 with both towers int8). So the image tower stays fp32 too. The CPU tier meets its budget without int8.
- **fp16 is lossless at half the size,** and q4f16 is nearly lossless at 422 MB. The user chose fp32 on desktop and fp16 on Android (D093).

### Tokenizers in .NET (E06-S03-T03)

`TokenizerCheck.cs` compares `Microsoft.ML.Tokenizers` 2.0.1 (`SentencePieceTokenizer`) with the Python tokenizers on all 215 queries:

| Tokenizer | Used by | Result |
|---|---|---|
| Gemma SentencePiece (lower-cased, `</s>` appended, padded to 64 with 0) | SigLIP 2 | **215/215 identical** |
| XLM-R SentencePiece (fairseq ID offset) | OpenCLIP XLM-R, MEXMA, jina-clip-v2 | 215/215 identical |
| XLM-V SentencePiece (fairseq ID offset) | MetaCLIP 2 worldwide | 215/215 identical |
| mT5 SentencePiece | MetaCLIP 2 mT5 | **Doesn't load:** `IndexOutOfRangeException` in `SentencePieceUnigramModel` |

The SigLIP 2 rule is 3 lines of code. MFC-18's `TextEncoder.Tokenize` already implements it.

## Android

MFC-18 measured the int8 SigLIP 2 base text tower on the Galaxy S21 FE: 50 ms per query and +435 MB PSS on the CPU provider. That file is the one that breaks Russian, so the phone needs the fp16 tower (539 MB on disk) instead. Its latency and memory there aren't measured.

- **Expect more memory than int8's +435 MB PSS,** since the weights are twice as large. That's fine on the 8 GB test phone but risky on 4 GB phones. q4f16 (422 MB, −1 point in Russian) is the fallback.
- **Latency:** ONNX Runtime has fp16 kernels on arm64, so it may be no slower than int8. That's still to be measured.
- **Re-test:** run MFC-18's spike page with the fp16 tower, `adb shell am start ... --es spike "onnx:siglip2:cpu"` after replacing `text_model_int8.onnx`. Optional for this spike; E20 needs it before it relies on the model.

## Follow-ups

- **E06-S03 (MFC-85):** use the onnx-community export (`onnx-community/siglip2-base-patch16-256-ONNX`: `vision_model.onnx` and `text_model.onnx`, or the fp16 text tower for Android) or the export from `export_onnx.py`, which produces the same output.
  - Preprocessing: resize to 256×256 without cropping (bilinear), scale to [−1, 1].
  - Queries: lower-case them, then tokenize with Gemma SentencePiece, append `</s>`, and pad with 0 to 64 tokens. The pooled output is the embedding.
  - Check the .NET output against `run_retrieval.py --onnx fp32:fp32` with the regression test (E06-S03-T05).
- **E06-S12 (MFC-88):** catalogue entry: Apache-2.0 for weights and code. Desktop file size is 355 + 1,077 MB; Android's text tower is 539 MB.
- **E06-S08:** measure decoding and resizing throughput in .NET on the CPU tier. The model alone has headroom.
- **E09:** Russian abstract and file-kind queries are weak in every model, so hybrid ranking with tags and descriptions matters for them.
- **E20 (Android):** measure the fp16 text tower on the phone, and the larger 768-dimension vectors in D084's search path.

## Drafted decisions

> ## D092 — Embedding model: SigLIP 2 base-patch16-256
> **Status:** Proposed (from E06-SP01 / MFC-53)
>
> Semantic search uses `google/siglip2-base-patch16-256` (Apache-2.0): its image tower embeds items and keyframes, and its text tower embeds queries, into 768-dimension vectors. Queries are lower-cased before tokenizing (Gemma SentencePiece, `</s>` appended, padded to 64 tokens), because the model was trained on lower-cased text; without it English recall@10 drops from 0.96 to 0.87.
>
> - **Measured (E01-S10 corpus):** recall@10 0.960 (en) / 0.815 (ru), MRR 0.984 / 0.837. That's 6 points ahead of every other multilingual candidate in English, and within 3 points of the best in Russian.
> - **Speed (ONNX Runtime 1.31.1, i7-14700HX / RTX 4070 Laptop):** 11.8 images/s on 8 CPU threads and 130 on the GPU; 26–32 ms per query on the CPU and 3.5 ms on the GPU. This meets the D031 budgets.
> - **Rejected:** SigLIP 2 base-384 (Russian +0.03, but about 2 images/s on the CPU tier); so400m (no better, 4× the compute); MetaCLIP 2 (CC-BY-NC, 7 points worse in English); OpenCLIP XLM-R (6 points worse in English); MEXMA-SigLIP 2 and the distilled multilingual CLIP (weaker); jina-clip-v2 (doesn't fit an 8 GB GPU comfortably, CC-BY-NC, not measured).
> - **Consequences:** vectors are 768-dimensional, 1.5 KB each in half precision (D067). 100k items × 3 vectors take about 460 MB in memory, and Android search time (D084) grows by the same factor of 1.5. The tokenizer runs through `Microsoft.ML.Tokenizers`, with token IDs identical to the reference on all evaluation queries.

> ## D093 — Embedding model precision: no int8 text tower
> **Status:** Proposed (from E06-SP01 / MFC-53)
>
> The SigLIP 2 towers ship unquantized. On desktop, both towers are fp32: image 355 MB, text 1,077 MB. On Android, the text tower is fp16: 539 MB, with retrieval identical to fp32.
>
> - **Why:** every int8 variant of the text tower tested (per tensor, per channel, MatMul only, and the publisher's int8 file that MFC-18 ran on the phone) keeps English recall@10 at 0.96 but drops Russian from 0.815 to about 0.55. The int8 image tower costs another 10 points in English.
> - **Fallback:** q4f16 (422 MB) scores 0.957 / 0.803 if memory on small phones forces it.
> - **To measure:** the fp16 text tower's latency and memory on the phone (E20). MFC-18's int8 numbers (50 ms, +435 MB PSS) no longer apply.

> ## D094 — Embedding quality baselines
> **Status:** Proposed (from E06-SP01 / MFC-53)
>
> `eval/baselines.json` sets the minimums for the E01-S10 public corpus: recall@10 ≥ **0.94** (en) and ≥ **0.79** (ru), MRR ≥ **0.96** (en) and ≥ **0.81** (ru). These are SigLIP 2 base-256's measured values (0.960 / 0.815 and 0.984 / 0.837) minus about 0.02. The E06-S03 acceptance criterion and the regression test (E06-S03-T05, `evaluate --fail-below-baseline`) use them.
>
> - **They catch the known failure modes:** forgetting to lower-case queries (en 0.87) and an int8 text tower (ru 0.55). They leave room for small differences in .NET preprocessing.
> - **Changing them:** a model change (E06-S07) sets new baselines from its own report.
> - **OCR:** the CER maximums stay empty until E06-SP04.
