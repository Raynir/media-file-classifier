# Evaluation corpus and quality metrics

A labelled set of media and queries for judging search and OCR quality by numbers (E01-S10). The model spikes (E06-SP01, SP02, SP04), the embedding regression test (E06-S03-T05) and relevance tuning (E09-S05) use it. The small edge-case corpus for unit tests is a different thing: `tests/SampleMedia` (E01-S11).

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
- **Languages:** English and Russian (D029), 105 and 110 queries. Most queries come in pairs with the same labels, so the two languages can be compared directly; some are language-specific (synonyms, text in a sign).
- **Categories:** `object`, `scene`, `synonym`, `text` (text visible in the image), `abstract` (mood or idea), `distractor` (look-alikes must rank lower), `media` (the kind of file, such as an animated flag or a phone screenshot). Reports break the scores down by category.
- `notes` records doubtful labels and why a look-alike isn't relevant.

## OCR ground truth

`eval/ocr-truth.json` has the exact text of 80 images: 60 synthetic ones (the text they were rendered from) and 20 real photos, transcribed and then checked by a second person (`"verified": true`).

- Lines are in reading order, separated by `\n`.
- Real photos: all text a viewer reads at normal size. Tiny incidental text (number plates, stickers, pole numbers, chalk marks) and reflections are left out.
- `scripts` is `latin`, `cyrillic` or both; CER is reported for `latin`, `cyrillic`, `mixed` and `all`.

## Metrics

- **Recall@10 (capped):** relevant items in the first 10 results, divided by the number of relevant items or 10, whichever is smaller. A perfect ranking scores 1 even for a query with more than 10 relevant items. Averaged over the queries of each language.
- **MRR:** one over the rank of the first relevant result, 0 if none is returned, over the first 100 results. Averaged per language.
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

Commit the reports of public runs, so the next run compares against a shared history. A second run of the same system within the same second gets a `-2` suffix instead of replacing the first report.

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

The model run is manual for now, because no model exists yet: the spike runs the models, writes a results file, and runs `evaluate` as above. To run the public subset in CI later (E01-S11-T02), a job needs:

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
