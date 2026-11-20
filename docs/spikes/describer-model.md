# Spike: describer model selection (MFC-105, E06-SP02)

**Unblocks:** E06-S04 Description and tagging (MFC-108), with its prompt (MFC-311) and field coverage (MFC-308). **Decisions:** drafted at the end, accepted by the user. **Status:** measured on desktop.

## Question

Which model should write the description, title, tags and lists (objects, scenes, actions, emotions) of each item, per route (D005: in-process ONNX, or an OpenAI-compatible server such as LM Studio) and hardware class (D031), and does the full analysis profile fit its D031 budget?

## Answer

| Route | Default | Option | Why |
|---|---|---|---|
| LM Studio (GPU) | **qwen3-vl-4b** with prompt C (`describe-2-draft-c`) | **gemma-4-e2b** for GPUs with less memory | Best blind score (146/150) and the fastest (7.5 s/image, 5.0 GB GPU memory). gemma-4-e2b needs 2.9 GB, scores 134 and takes 13.9 s. |
| In-process (every class, the default on the CPU tier) | **Florence-2 base fp32** on the CPU | **Florence-2 large fp32**, chosen in the analysis profile | 5.9 s/image and 2.2 GB RAM. Large is somewhat better (95 against 89/150, five 1-scores against 11) but takes 10.8 s and 5.4 GB. |

- **The D031 full-profile budget is missed.** "≥ 1 image/s on the GPU tier" can't hold with any describer: the fastest takes 6.5–7.5 s per image. The describer gets its own budget instead: **≤ 10 s per image on the GPU tier**, and the 1 image/s applies to the profile without the describer.
- **LM Studio on a CPU-only machine is too slow** to be a default: qwen3-vl-4b takes 47.6 s and gemma-4-e2b 35.5 s per image, and both still put 1.2–1.4 GB on the GPU. On the CPU tier the in-process route is the default.
- **qwen3-vl-4b's Russian is its weak spot:** 18 of 50 answers had a poor Russian title or tag (wrong case, a wrong or invented word), against 4–7 for Gemma. Gemma makes more content mistakes instead: a tram called a bus, a duck called a swimmer, a lynx called a canid. The tag dictionary (D349) can correct the Russian forms; it can't correct a wrong subject.
- **Rejected:** MiniCPM-V 4 (valid JSON on 20% of the samples even after a retry, almost no Russian, labels such as "bear1"; 59/150) and gemma-4-e4b as the default (140/150 and better Russian, but 21.4 s per image).

## Candidates

| System | Route | Licence | Variants run |
|---|---|---|---|
| `qwen/qwen3-vl-4b` | LM Studio | Apache-2.0 | prompts A, B, C, no presence penalty; CPU (10 samples) |
| `google/gemma-4-e4b` | LM Studio | Gemma 4 (Apache-2.0) | prompt A |
| `google/gemma-4-e2b` | LM Studio | Gemma 4 (Apache-2.0) | prompt A; CPU (10 samples) |
| `minicpm-v-4` (Q4_K_M with its mmproj) | LM Studio | MiniCPM model licence | prompt A |
| Florence-2 base and large (`onnx-community/Florence-2-*-ft`) | in-process, ONNX Runtime | MIT | fp32 and int8; CPU, DirectML, hybrid |
| `qwen3-vl-4b-describe-1` | the app's own describe-1 output (`eval/analysis`) | – | baseline, English only |

## How it was measured

- **Samples:** 50 items of the evaluation corpus ([evaluation-corpus.md](../evaluation-corpus.md)), spread evenly over the IDs: 22 photos, 5 screenshots, 5 documents, 8 GIFs, 10 videos (`tools/describer-spike/samples.py`). The model sees what the app sends: a still at 1024 px on its longest edge, or the middle frame of a GIF or video.
- **Machine:** i7-14700HX, 32 GB, RTX 4070 Laptop 8 GB. Seconds are the median wall-clock time per image after one warm-up request.
- **LM Studio** (`run_vlm.py`): sent like the app's `OpenAiCompatibleImageDescriber`: the schema as `response_format`, temperature 0.2, presence penalty 1.5 (D350), output limits (D344), one retry with the reason when the answer is unusable. GPU memory is `nvidia-smi` before and after loading.
- **Florence-2** (`run_florence.py`): image features once, then three task prompts with greedy decoding and no repeated 3-grams: `<MORE_DETAILED_CAPTION>` → description, `<CAPTION>` → summary, `<OD>` → objects, whose labels also become the English tags.
- **Quality, blind** (`blind_scores.py`): eight systems, each sample's outputs under labels A–H shuffled per sample, judged by five reviewer agents with ten samples each, which saw only their samples' images (photos and frames at 512 px, screenshots and documents at 1024 px) and the outputs. Rubric: 1 = wrong or useless for search, 2 = usable, 3 = good, with a one-line reason, plus flags for hallucinations, Russian quality (a real word, nominative case, not transliterated) and empty fields. The totals and the samples where the systems disagreed most went to the user to spot-check. The scores are in `eval/describer-spike/blind-scores.json`, keyed like the side-by-side page (`report.py --html`), so they load into it.

Run the scripts from the repository root with `models/.venv/Scripts/python` (onnxruntime-directml, tokenizers, numpy, pillow, psutil). The raw answers are in `eval/describer-spike/results/`.

## Results

### Quality (blind scores, 50 samples)

| System | Total | Mean | 3s | 1s | Hallucinations | Russian ok/poor/none | Photo | Screenshot | Document | GIF | Video |
|---|---|---|---|---|---|---|---|---|---|---|---|
| qwen3-vl-4b, prompt A | 148/150 | 2.96 | 48 | 0 | 6 | 29/21/0 | 2.91 | 3.00 | 3.00 | 3.00 | 3.00 |
| **qwen3-vl-4b, prompt C** | 146/150 | 2.92 | 46 | 0 | 5 | 32/18/0 | 2.86 | 3.00 | 3.00 | 3.00 | 2.90 |
| gemma-4-e4b | 140/150 | 2.80 | 42 | 2 | 5 | 46/4/0 | 2.91 | 3.00 | 3.00 | 2.25 | 2.80 |
| gemma-4-e2b | 134/150 | 2.68 | 37 | 3 | 9 | 43/7/0 | 2.68 | 3.00 | 2.80 | 2.38 | 2.70 |
| qwen3-vl-4b, describe-1 (app today) | 125/150 | 2.50 | 26 | 1 | 4 | –/–/50 | 2.64 | 2.40 | 2.40 | 2.50 | 2.30 |
| Florence-2 large fp32 | 95/150 | 1.90 | 0 | 5 | 9 | –/–/50 | 2.00 | 1.80 | 1.80 | 1.75 | 1.90 |
| **Florence-2 base fp32** | 89/150 | 1.78 | 0 | 11 | 16 | –/–/50 | 1.91 | 1.60 | 1.60 | 1.62 | 1.80 |
| MiniCPM-V 4 | 59/150 | 1.18 | 0 | 41 | 4 | 1/9/40 | 1.18 | 1.00 | 1.00 | 1.00 | 1.50 |

Prompt A scores two points above C, but the reviewers didn't mark A down for its empty lists: all 28 of its answers with an empty list scored 3. C is chosen for filling them (below). Florence-2 is weakest on screenshots and documents, where it describes the layout but not what the text says.

### Speed, memory and field coverage

Fields: the share of samples where the field is filled. "Tags ru" counts samples where every tag has a Russian form.

| System | n | Valid JSON (1st try / after retry) | s/image median (mean, p90) | title | title_ru | descr. | summary | tags | tags ru | objects | scenes | actions | emotions | tags/item | Memory |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| qwen3-vl-4b, C | 50 | 100% / 100% | 7.5 (7.9, 10.8) | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 94% | 92% | 10.5 | 5,037 MiB GPU |
| qwen3-vl-4b, A | 50 | 100% / 100% | 7.2 (7.1, 9.7) | 100% | 100% | 100% | 100% | 100% | 100% | 50% | 50% | 44% | 46% | 10.0 | 4,938 MiB GPU |
| gemma-4-e2b | 50 | 100% / 100% | 13.9 (14.6, 22.8) | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 98% | 7.0 | 2,926 MiB GPU |
| gemma-4-e4b | 50 | 100% / 100% | 21.4 (21.5, 25.4) | 100% | 100% | 100% | 100% | 100% | 100% | 98% | 92% | 70% | 70% | 6.4 | 4,501 MiB GPU |
| minicpm-v-4 | 50 | 2% / 20% | 6.5 (6.6, 9.7) | 20% | 20% | 20% | 20% | 4% | 4% | 12% | 14% | 4% | 2% | 0.1 | 3,427 MiB GPU |
| qwen3-vl-4b, A, CPU | 10 | 100% / 100% | 47.6 (45.6, 55.4) | 100% | 100% | 100% | 100% | 100% | 100% | 60% | 60% | 60% | 60% | 8.3 | 1,380 MiB GPU |
| gemma-4-e2b, CPU | 10 | 100% / 100% | 35.5 (36.2, 39.5) | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 7.4 | 1,188 MiB GPU |
| Florence-2 base fp32, CPU | 50 | n/a | 5.9 (5.9, 6.5) | – | – | 100% | 100% | 100% | – | 100% | – | – | – | 1.4 | 2,229 MiB RAM |
| Florence-2 base int8, CPU | 50 | n/a | 5.7 (5.7, 6.3) | – | – | 100% | 100% | 100% | – | 100% | – | – | – | 1.3 | 1,378 MiB RAM |
| Florence-2 large fp32, CPU | 50 | n/a | 10.8 (11.7, 15.6) | – | – | 100% | 100% | 100% | – | 100% | – | – | – | 1.6 | 5,351 MiB RAM |
| Florence-2 large int8, CPU | 50 | n/a | 8.8 (8.8, 9.3) | – | – | 100% | 100% | 100% | – | 100% | – | – | – | 1.4 | 2,804 MiB RAM |
| Florence-2 base fp32, hybrid | 50 | n/a | 5.9 (5.5, 11.4) | – | – | 100% | 100% | 100% | – | 100% | – | – | – | 1.4 | 1,451 MiB GPU |
| Florence-2 large fp32, hybrid | 50 | n/a | 18.4 (15.2, 30.2) | – | – | 100% | 100% | 100% | – | 100% | – | – | – | 1.6 | 3,993 MiB GPU |

The CPU-only LM Studio runs still put 1.2–1.4 GB on the GPU (probably the vision projector), so a machine with no GPU at all would be slower still. With 8 threads instead of the default, Florence-2 base takes 6.3 s and large 16.4 s: no gain.

### Field coverage per provider (for MFC-308)

| Field | Vision-language model (prompt C) | Florence-2 (in-process) |
|---|---|---|
| title | written | **empty** |
| title_ru, Russian tag forms | written by the model (D348); quality varies, see above | **empty**: the tag dictionary or a translation fills them, marked as derived (D349) |
| description | 2–4 sentences | `<MORE_DETAILED_CAPTION>`, one paragraph |
| summary | one sentence | `<CAPTION>` |
| tags | up to 15 `{en, ru}` pairs, about 10 per item | the `<OD>` labels, lower-cased, about 1.4 per item, English only |
| objects | always (minItems 1) | the `<OD>` labels |
| scenes | always (minItems 1) | **empty** |
| actions, emotions | 94% / 92% (left empty where nothing fits, such as a document) | **empty** |

## Prompt C

`tools/describer-spike/prompt.py` holds three variants of the draft `describe-2` prompt; C (`describe-2-draft-c`) is the input for MFC-311. Against the app's describe-1 it adds the English and Russian forms of the title and tags in one answer (D348), rules that make tags useful as keywords (singular, lower case, no generic words, no near-duplicates, Russian in the nominative case and not transliterated), and the kind of media as context. Limits stay as in describe-1: 15 tags, 30 items per list, 4,096 output tokens (D344, D350).

- **A** ended the list instruction with "an empty list if nothing fits". qwen3-vl-4b then filled 15 tag pairs and left all four lists empty on half the samples: the presence penalty discourages repeating words the tags already used.
- **B** moved the lists before the tags (in the text and the schema's property order). Worse. Dropping the presence penalty didn't help either.
- **C** is A without that sentence, with describe-1's wording for the lists and `minItems: 1` on objects and scenes, so a grammar-constrained server can't return them empty. Actions and emotions stay optional: a document has no mood to invent. Objects and scenes went from 50% to 100% filled, actions and emotions from 44–46% to 92–94%, at 0.3 s more per image.

## Florence-2 on DirectML

The merged decoder of the onnx-community export **produces random tokens on DirectML** at every graph optimisation level, while the vision encoder and the text encoder give the same outputs as on the CPU. The hybrid run (encoders on DirectML, decoder on the CPU) is no faster: base 5.9 s, large 18.4 s, since most of the time is the decoder. Not tried: the separate `decoder_model` and `decoder_with_past_model` files (about 2 GB more to download). Until then, the in-process describer runs on the CPU on every hardware class.

## Follow-ups

- **MFC-311 (E06-S04-T04):** turn prompt C into `describe-2` in the app (`ImageDescriptionContract`), with `title_ru` and `{en, ru}` tags.
- **MFC-308:** the field coverage table above; Florence-2 leaves title, Russian forms, scenes, actions and emotions empty.
- **E06-S04-T09 / D349:** the tag dictionary should also be able to correct the vision-language model's Russian forms, since qwen3-vl-4b's are poor on about a third of the items.
- **E06-S04-T06:** measure the full profile against the new budgets on both tiers and record the CPU-tier number in D031.
- Not measured: zero-shot SigLIP 2 labels for scenes, actions and moods with Florence-2; Florence-2 on the GPU with the split decoder files.

## Drafted decisions

- **DNEW-1 — Default describer per route and hardware class.** The vision-language model route defaults to `qwen/qwen3-vl-4b` in LM Studio with prompt `describe-2-draft-c`; `google/gemma-4-e2b` is the documented option for GPUs with less memory (2.9 GB against 5.0 GB). The in-process route uses Florence-2 base fp32 on the CPU on every hardware class, and is the default on the CPU tier, where LM Studio takes 36–48 s per image; Florence-2 large fp32 can be chosen in the analysis profile. Measured on 50 corpus samples (i7-14700HX, RTX 4070 Laptop), blind scores out of 150: qwen3-vl-4b 146 at 7.5 s/image, gemma-4-e4b 140 at 21.4 s, gemma-4-e2b 134 at 13.9 s, Florence-2 large 95 at 10.8 s (CPU), base 89 at 5.9 s (CPU). Rejected: MiniCPM-V 4 (valid JSON on 20% of the samples, almost no Russian; 59/150), gemma-4-e4b as the default (3 times slower). Consequence: qwen3-vl-4b's Russian forms are poor on about a third of the items, so the tag dictionary (D349) matters for this route too. Florence-2 stays on the CPU because the onnx-community merged decoder produces wrong tokens on DirectML (encoders on DirectML with the decoder on the CPU is no faster).
- **DNEW-2 — The describer has its own throughput budget.** The D031 full-profile budget (GPU tier ≥ 1 image/s) can't be met with any describer, since the fastest takes 6.5–7.5 s per image. The describer gets its own budget of ≤ 10 s per image on the GPU tier, and ≥ 1 image/s applies to the full profile without the describer. Amends D031. Consequence: describing a large library takes hours (10,000 items ≈ 21 hours at 7.5 s), so description must run in the background without blocking search.

Details of the scripts: `tools/describer-spike/`.
