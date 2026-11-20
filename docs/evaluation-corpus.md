# Evaluation corpus and quality metrics

A labelled set of media and queries for judging search and OCR quality by numbers (E01-S10). The model spikes (E06-SP01, SP02 in [spikes/describer-model.md](spikes/describer-model.md), SP04), the embedding regression test (E06-S03-T05) and relevance tuning (E09-S05) use it. The small edge-case corpus for unit tests is a different thing: `tests/SampleMedia` (E01-S11).

## What's in it

| Part | Where | In Git? |
|------|-------|---------|
| Manifest: one entry per file, with source, author, licence, changes and SHA-256 | `eval/manifest.json` | yes |
| Queries with every relevant item | `eval/queries.json` | yes |
| OCR ground truth | `eval/ocr-truth.json` | yes |
| Baseline thresholds | `eval/baselines.json` | yes |
| Data set description | `eval/dataset.json` | yes |
| Release pointer: tag, asset, SHA-256 | `eval/corpus-release.json` | yes |
| Reports of public runs | `eval/reports/` | yes |
| The media files | `eval/corpus/` | no, fetched |
| Private data sets | `eval/private/` or any folder outside the repository | no |

The public subset has 351 files:

| Kind | Files | Notes |
|------|------:|-------|
| photo | 212 | Wikimedia Commons, at most 1600 px on the long side. About 40 show Latin or Cyrillic signs. |
| screenshot, document | 26 + 34 | Synthetic, rendered from known English, Russian and mixed text; 22 have photo-like distortions. |
| gif | 34 | Animated GIFs from Commons, unchanged. |
| video | 45 | MP4 clips of at most 10 s and 1280x720 (H.264/AAC), cut from Commons videos. |

Look-alikes are there on purpose: cats and lynxes, borscht and cold beet soup, St Basil's and other onion domes, the cruiser *Aurora* and the aurora, the METRO supermarket and the metro.

## Fetching the corpus

The files are a zip on the GitHub release `eval-corpus-v1`. The repository is private, so the [GitHub CLI](https://cli.github.com) is needed (`gh auth login` once). From the repository root:

```powershell
pwsh tools/eval-corpus/fetch-corpus.ps1
```

It downloads about 240 MB, checks the zip's SHA-256, unpacks it to `eval/corpus/`, and checks each file against the manifest. Running it again does nothing if the corpus is complete; `-Force` fetches it again.

## Licences and attribution

Accepted: CC0, Public Domain, CC BY and CC BY-SA (2.0–4.0, ported versions too), from Wikimedia Commons, with the licence read from the Commons API for each file. Files with restrictions other than personality rights are refused. The synthetic files are CC0.

- **Using** the files for measurements creates no obligations.
- **Sharing** them, including the zip, a thumbnail in a report, or a post: credit the author, link the source and the licence, and say what changed. The manifest and `ATTRIBUTION.md` in the zip have all of that. Changed CC BY-SA files (downscaled photos, cut clips) stay CC BY-SA. Nothing here affects the app's licence, because the corpus isn't shipped with the app.
- Photos of identifiable people and brands are for evaluation, not for marketing.
- If Commons deletes a file, drop it from the next corpus version.

## Queries and labels

`eval/queries.json` has one query per line:

```json
{"id": "ru-031", "language": "ru", "category": "scene", "text": "снег", "relevant": ["photo-018", "video-012"], "notes": "Reviewed: photo-082 (metro entrance) has too little snow to count."}
```

- **Relevance is binary**, and **every** relevant item of the corpus is listed, not just one.
- **Languages:** English and Russian (D029), 139 and 142 queries. Most queries come in pairs with the same labels, so the two languages can be compared directly; some are language-specific (synonyms, text in a sign).
- **Categories:** `object`, `scene`, `synonym`, `text` (text visible in the image), `abstract` (mood or idea), `distractor` (look-alikes must rank lower), `media` (the kind of file, such as an animated flag or a phone screenshot). Reports break the scores down by category.
- **Search-specific categories (E09-S05):** `filename` (a fragment of the file name, such as `Pelmeni`, `2016-10-03` or `Донецк`; relevant are the items whose file name contains it, where the file name is the Commons title, see "Keyword and hybrid search" below), `tag` (one tag-style word, such as `autumn` or `чёрно-белое`; relevant are the items with that manifest tag, reviewed) and `none` (nothing in the corpus matches, such as `a giraffe` or `фывапролдж`). A `none` query lists no relevant items; it is left out of recall@10 and MRR and scored by **none-shown** below.
- `notes` records doubtful labels and why a look-alike isn't relevant.

## OCR ground truth

`eval/ocr-truth.json` has the exact text of 80 images: 60 synthetic ones (the text they were rendered from) and 20 real photos, transcribed and then checked by a second person (`"verified": true`).

- Lines are in reading order, separated by `\n`.
- Real photos: all text a viewer reads at normal size. Tiny incidental text (number plates, stickers, pole numbers, chalk marks) and reflections are left out.
- `scripts` is `latin`, `cyrillic` or both; CER is reported for `latin`, `cyrillic`, `mixed` and `all`.

## Metrics

- **Recall@10 (capped):** relevant items in the first 10 results, divided by the number of relevant items or 10, whichever is smaller. A perfect ranking scores 1 even for a query with more than 10 relevant items. Averaged over the queries of each language.
- **MRR:** one over the rank of the first relevant result, 0 if none is returned, over the first 100 results. Averaged per language.
- **None-shown:** the share of the `none` queries that return any result; lower is better. Recall@10 and MRR average only the other queries.
- **CER:** the edit distance between the OCR output and the ground truth, divided by the length of the ground truth, summed over images (total edits over total characters). Both texts are Unicode-normalized (NFC) and every run of whitespace, line breaks included, becomes one space. Case, punctuation and ё are kept. Missing output counts as fully wrong.

Every value is rounded to four decimals, and the computation has no randomness, so two runs on the same input give identical numbers.

## Running an evaluation

The runner is `tools/MediaFileClassifier.Evaluation`. It doesn't run models itself: a system under test writes a results file, and the runner scores it. That way the model spike can compute results in Python, and the app can be plugged in later through `IRetrievalSystem` and `IOcrEngine` (E06-S03-T05).

A results file:

```json
{
  "system": "siglip2-base-patch16-256",
  "search": { "en-001": ["photo-001", "gif-002", "video-003"] },
  "ocr": { "synth-001": "Emma\nAre we still on for dinner tonight?" }
}
```

`search` maps each query ID to item IDs, best first (up to 100). `ocr` maps image IDs to recognized text. Either part may be left out. A missing query or image scores as an empty result.

From the repository root:

```powershell
dotnet run --project tools/MediaFileClassifier.Evaluation -- validate --files
dotnet run --project tools/MediaFileClassifier.Evaluation -- evaluate --results path/to/results.json
```

- `validate` checks IDs, labels, minimums and licences; `--files` also checks the media against their checksums.
- `evaluate` writes `eval/reports/<yyyy-MM-dd_HHmmss>_<system>.json` and `.md`. The report shows each metric, the change from the previous report of the same system, the baseline and its status, scores per category, and the weakest queries. If the labels changed since the previous run, the report says so, because the deltas then compare different questions.
- `--fail-below-baseline` exits with code 3 when a metric is worse than its baseline, for CI later.

To score the app's own SigLIP 2 pipeline instead of a results file (E06-S03-T05), give the model folder:

```powershell
dotnet run -c Release --project tools/MediaFileClassifier.Evaluation -- evaluate --system siglip2 --models models/siglip2 --fail-below-baseline
```

`Siglip2System` embeds each item with `Siglip2EmbeddingProvider`: one vector for a photo, screenshot or document, and the mean of the keyframes' vectors for a GIF or video, sampled by the app's `FrameSampler` (videos need FFmpeg). Each query is encoded by the text tower and ranked by cosine similarity. It takes about 1.5 minutes on an i7-14700HX. It scores the content categories only: the search-specific ones (`filename`, `tag`, `none`, E09-S05) ask for keyword search, which vector similarity alone can't do, so the D094 minimums keep their meaning.

`threshold --models models/siglip2` measures the semantic relevance cut (E09-S01-T03): for raw, centred (minus the query's mean over the corpus) and z-scored similarity, it prints how many relevant items of the real queries stay visible and how many results 20 built-in nonsense queries still get at each threshold, then the same for the app's rule in `SemanticRelevanceThresholds`. First run (2026-11-09): raw cosine overlaps badly (nonsense top-1 0.07–0.12, relevant median 0.12 en / 0.10 ru); the app's rule, centred ≥ 0.07 or raw ≥ 0.13, keeps 88% (en) and 89% (ru) of the relevant items in the top 10, and 2 of 20 nonsense queries show one item.

Commit the reports of public runs, so the next run compares against a shared history. A second run of the same system within the same second gets a `-2` suffix instead of replacing the first report.

### Keyword and hybrid search (E09-S05)

The app's own search runs over the corpus as `--system app-hybrid` (what the search bar does), `app-vector` (vector results alone) or `app-keyword` (keyword results alone). `AppSearchSystem` builds a temporary library with the app's repositories: each item gets its SigLIP 2 vector (as `Siglip2System` computes it) and, as its metadata, what the app's describer and OCR wrote for it. It then searches with the app's `SearchIndex` the way `SearchMediaQueryHandler` does: the normalised text, the lower-cased query encoded by the text tower, and the relevance cut on hits only the vector search found. An item's file name is its Commons title with the corpus file's extension (`Gatta randagia Livorno.jpg`); the synthetic items keep their own names (`synth-001.png`).

The AI analysis is stored once, so every run indexes the same text. `analyze` runs the app's `OpenAiCompatibleImageDescriber` and `OpenAiCompatibleOcrProvider` over the corpus, with the inputs `ItemAnalyzers` gives them: a still image is described at 1024 px and read at 2048 px on its longest edge; a video or GIF is described from its middle keyframe and read on every keyframe. It writes `eval/analysis/<model>.json` after each item and skips the items already there, so a stopped run resumes. The committed file is from `qwen/qwen3-vl-4b` in LM Studio, describing in English (the profile language); about 11 s per still image on a GPU with 8 GB. `qwen/qwen3-vl-8b` was too slow there: OCR images of 2048 px filled the GPU memory, and a photo took 1 to 2 minutes.

```powershell
dotnet run -c Release --project tools/MediaFileClassifier.Evaluation -- analyze                  # LM Studio at localhost:1234, qwen/qwen3-vl-4b
dotnet run -c Release --project tools/MediaFileClassifier.Evaluation -- evaluate --system app-hybrid --models models/siglip2
dotnet run -c Release --project tools/MediaFileClassifier.Evaluation -- evaluate --system app-hybrid --models models/siglip2 --rrf-k 30 --field-weights title=4,tags=5
```

`eval/analysis/qwen_qwen3-vl-4b-describe-2.json` is the same model with the describe-2 prompt (E06-S04-T02, the app's default), which also writes a Russian title and a Russian form per tag (`titleRu`, `tagsRu`); `analyze --ocr-from eval/analysis/qwen_qwen3-vl-4b.json` reused the OCR text. `AppSearchSystem` stores those tags as one AI run, through `TagNormalizer` and `TagAssignment.Ai` (the model's form, else the dictionary's), and the Russian title with the title, as the describer does (E06-S04-T09). Results of 2026-11-19 (reports `2026-11-19_123251_app-keyword`, `2026-11-19_124635_app-hybrid`), describe-1 analysis → describe-2 analysis, both with the tag dictionary and Russian stems:

| System | en recall@10 | en MRR | ru recall@10 | ru MRR |
|---|--:|--:|--:|--:|
| app-keyword | 0.707 → 0.671 | 0.834 → 0.758 | 0.553 → 0.557 | 0.625 → 0.612 |
| app-hybrid | 0.921 → 0.917 | 0.972 → 0.957 | 0.841 → 0.860 | 0.899 → 0.887 |

Russian keyword search went from 0.16 / 0.21 (the 2026-11-14 baseline, English metadata only) to about 0.55 / 0.61 with the dictionary's tag forms and stems; describe-2's own forms add little on top. English keyword search alone is lower with describe-2 (abstract queries MRR 0.75 → 0.38, media 0.75 → 0.50); the search bar's hybrid results stay within 0.015. The app-keyword baselines were reset to describe-2's values minus 0.02 (user's choice); tuning the keyword field weights for describe-2 is a follow-up.

The ranking settings default to the app's (`SearchRankingOptions.Default`, `SemanticRelevanceThresholds`); `--rrf-k`, `--keyword-weight`, `--vector-weight`, `--field-weights` and `--threshold` (`centred,raw` or `none`) try other values without a rebuild. The report lists the settings it ran with. `tune` scores a fixed grid of them in one process and writes `reports/<date>_tune-app-hybrid.md` (E09-S05-T03).

### OCR engines (E06-S05)

`ocr` runs one OCR engine over the 80 ground-truth images and the 164 photos without text tags, with the input the app gives it (a still image at most 2048 px on its longest edge), and writes `eval/ocr-results/<engine>.json` for `evaluate --results`. It prints the time per image and every no-text photo that got text; those are checked by eye, since many have real incidental text (ear tags, number plates).

```powershell
dotnet run -c Release --project tools/MediaFileClassifier.Evaluation -- ocr --system rapidocr --models models/pp-ocrv5 --ep cpu
dotnet run -c Release --project tools/MediaFileClassifier.Evaluation -p:OnnxGpu=true -- ocr --system rapidocr --models models/pp-ocrv5 --ep directml
dotnet run -c Release --project tools/MediaFileClassifier.Evaluation -- ocr --system vlm      # LM Studio, qwen/qwen3-vl-4b
```

`models/pp-ocrv5` holds `ch_PP-OCRv5_det_mobile.onnx` and `eslav_PP-OCRv5_rec_mobile.onnx` from RapidOCR. Results of 2026-11-15 (MFC-331, i7-14700HX, 8 GB laptop GPU), CER Latin / Cyrillic / mixed / all, then ms per image and no-text photos with output:

| Engine | CER | CPU | GPU | No-text with output |
|---|---|---|---|---|
| RapidOCR, detector at most 960 px (app default) | 0.036 / 0.095 / 0.083 / 0.066 | 1,955 | 200 (DirectML) | 37 |
| RapidOCR, detector at most 2000 px (RapidOCR default) | 0.043 / 0.103 / 0.155 / 0.076 | 4,049 | 292 (DirectML) | 65 |
| qwen/qwen3-vl-4b in LM Studio | 0.024 / 0.058 / 0.353 / 0.052 | — | 3,875 | 41 |

RapidOCR is the default OCR engine on every hardware class; the vision-language model is chosen by naming its provider in the analysis profile.

With the short-line guard (MFC-334: a line needs two letters or digits, and three or fewer need confidence 0.8) the 960 px run reads 0.037 / 0.096 / 0.083 / 0.067, and 27 of the no-text photos have output instead of 37. All ten photos listed as stray-mark cases (photo-011, -034, -058, -070, -071, -078, -090, -098, -099, -123) give no text. The guard also drops a few real one-character lines such as a lone quantity "1" or "OK".

### Baseline runs

`baselines.json` `references` pins one report per system, by name: its untuned baseline run. Every later run of that system shows each headline metric's change from it, both in the console and in the report's summary table ("Baseline run" and "vs baseline run"), next to the change from the previous run. If the labels changed since the baseline run, the report says so.

## Baselines

`eval/baselines.json` holds per-language minimums for recall@10 and MRR, and per-script maximums for CER. E06-SP01 (MFC-53) set the search values from SigLIP 2 base-patch16-256, at about 0.02 below what it measured: recall@10 ≥ 0.94 (en) and ≥ 0.79 (ru), MRR ≥ 0.96 and ≥ 0.81 ([docs/spikes/embedding-model.md](spikes/embedding-model.md)). The CER maximums stay empty until the OCR spike (E06-SP04). Later quality criteria refer to these values. When you change one, write in `note` which report set it.

## Private data sets (E01-S10-T05)

Personal media never goes into the repository. To measure on your own photos:

1. Create a data set next to, not inside, the media. Use `eval/private/<name>` (git-ignored) or a folder outside the repository:

   ```powershell
   dotnet run --project tools/MediaFileClassifier.Evaluation -- init-private --media "D:\Photos" --data eval/private/home
   ```

   This lists the media into `manifest.json` (IDs `p-0001`…, paths relative to the media folder, checksums) and writes `dataset.json` with `"private": true` and the media folder. Nothing is copied. Both `--media` and `--data` are required. Folders and files it can't read are skipped with a message. Every data folder needs a `dataset.json`; without it the runner stops rather than checking a private set as the public one.

2. Add queries to `queries.json`, and OCR text to `ocr-truth.json` if wanted, in the formats above. A private set has no minimum sizes and no licence checks.
3. Produce a results file with the system under test, then run:

   ```powershell
   dotnet run --project tools/MediaFileClassifier.Evaluation -- evaluate --data eval/private/home --results results.json
   ```

   Reports go to `eval/private/home/reports/`.

`git status` stays clean, because `eval/private/` and `eval/corpus/` are in `.gitignore`. `init-private` warns when the data folder is inside the repository but not under `eval/private/`. A test checks that both patterns stay in `.gitignore`.

## CI

The data-consistency check runs in CI on every build: `PublicDataSetTests` validates the committed manifest, queries and OCR truth without the media.

The model run is manual. The embedding regression test (E06-S03-T05) is the command above, run after any change to the SigLIP 2 model, its preprocessing, the tokenizer or the keyframe sampler. The same run is the xunit test `Siglip2QualityTests` in `MediaFileClassifier.Evaluation.Tests`. It is opt-in (`MFC_QUALITY_TESTS=1`, see [Development workflow](development-workflow.md#opt-in-tests)) and also skips when `models/siglip2` (or `MFC_SIGLIP2_DIR`) or `eval/corpus` is missing, so it never runs in CI today. The first run's numbers, against the D094 baselines: recall@10 0.959 (en) and 0.816 (ru), MRR 0.983 and 0.831. To run the public subset in CI later (E01-S11-T02), a job needs:

- the corpus: `fetch-corpus.ps1` with `GH_TOKEN: ${{ github.token }}`, cached by the release tag;
- the model files, cached by model hash (D030), and a CPU ONNX runtime;
- a step that writes the results file, then `evaluate --fail-below-baseline`;
- a manual or nightly trigger rather than every pull request, to save Actions minutes on GitHub Free. The upload of the report as an artifact replaces committing it.

## Building a new corpus version

Only to change the corpus. It needs Python 3, Pillow and ffmpeg, and fonts from the Windows fonts folder.

1. Edit `eval/manifest.json`: add or remove entries with `id`, `kind`, `file`, `description`, `tags` and `source.commonsTitle` (and `clip` for videos).
2. `python tools/eval-corpus/generate_synthetic.py eval/corpus` (if the synthetic images changed).
3. `python tools/eval-corpus/build_corpus.py eval/corpus --zip eval-corpus-v2.zip`. It refuses unaccepted licences, fills in the author, licence, changes and checksums, and writes `ATTRIBUTION.md`.
4. Publish the zip as a new release, update `eval/corpus-release.json`, and check the labels of new items.
