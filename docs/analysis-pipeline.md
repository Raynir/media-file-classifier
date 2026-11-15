# Analysis pipeline (E06-S01)

The pipeline runs the library's analyzers on items and stores what they find. Analysis starts only with `AnalyzeItemsCommand`. The library's analysis profile says which analyzers run, in what order, and with which provider.

## Pieces

| Piece | Project | What it does |
|---|---|---|
| `AnalysisProfile`, `AnalyzerSettings`, `AnalyzerKeys` | `Domain/Analysis` | The profile, stored in the library settings JSON. The default is embedding, then describer (20 tags at most), then OCR, all enabled, with the provider picked automatically. |
| `AnalyzerState`, `AnalyzerStatus`, `AnalyzerReasons` | `Domain/Analysis` | How each analyzer last ended for an item: one row per item and analyzer in `analyzer_state` (schema 10). |
| `IAnalyzerStateRepository` (`IOpenLibrary.AnalyzerStates`) | `Infrastructure.Abstractions` | Reads and writes states. `ApplyRunAsync` stores a run, its values and tags, and the analyzer's state in one write. |
| `AnalyzeItemsCommand` | `UseCases/Analysis/AnalyzeItems` | Queues an `analysis` job with one `analysis.item` work item per item, in the AI pool. It can name analyzers, e.g. only `ocr` to retry OCR. |
| `AnalysisStage` | `UseCases/Analysis/Pipeline` | The stage handler: runs the analyzers of one item and sets its status. |
| `AnalysisInputs` | same | One decode of a still image, or one sampling of the keyframes, shared by an item's analyzers. |
| `EmbeddingItemAnalyzer`, `DescriberItemAnalyzer`, `OcrItemAnalyzer` | same (internal) | The analyzers. |

## What one item goes through

1. The stage loads the item: Pending, Indexed, or Error after an earlier analysis. It detects the file's format and reads its technical metadata.
2. It prepares the inputs. A still image is decoded once, at the size the enabled analyzers need together: the embedding needs a short side of 512 (D244), the describer a longest edge of 1024, OCR 2048. Each analyzer gets a copy shrunk to its own size. A video or animated image is sampled once, and its keyframes are stored.
3. It runs each requested analyzer in profile order:
   - **Embedding:** a still image's vector, or a vector per keyframe and their mean (E06-S03).
   - **Describer:** a title, a description, a summary, objects, scenes, actions, emotions, and tags (cut to `MaxTags`). A video is described from its middle keyframe.
   - **OCR:** the text of a still image, or the distinct lines of every keyframe joined with new lines. A run with no text is still applied, so it replaces an earlier run's text.
   - Describer and OCR results are an `AnalysisRun` with the model registration and the prompt version (provenance), applied with `ItemMetadata.ApplyRun`. User edits stay, and older runs are pruned to the library's retention.
4. Each analyzer's result and state are stored in their own write. A cancelled item keeps the results of the analyzers that finished; running the item again replaces them.
5. It sets the item's status, then rethrows a held failure (see below).

## Analyzer states

| Status | When | Retryable |
|---|---|---|
| Done | The result is stored. `RunId` points at the describer's or OCR's run. | — |
| Skipped `Disabled` | Off in the profile. | No |
| Skipped `NoProvider` | No provider has the capability, or the profile names one that isn't registered. | Yes |
| Skipped `UnsupportedPlatform` | Providers exist, but none runs here. | No |
| Skipped `ModelMissing` | The model isn't installed (D037). | Yes |
| Skipped `ProviderUnavailable` | The provider's circuit breaker is open (E05-S04), so it wasn't called. | Yes |
| Skipped `NoFrames` | A video's describer or OCR, when no keyframes could be read. | Yes |
| Retrying | A passing failure (unreachable, timeout, server error, locked file). The scheduler retries the work item. | Yes |
| Failed | The retries gave up, or the failure can't pass (invalid input or output, damaged file). | Unless permanent |

## Partial success and the item's status

- **Indexed:** the run included the embedding, and the embedding is done, or skipped for good (disabled, no provider, unsupported platform). The other analyzers' outcome doesn't matter, so an OCR failure alone leaves the item Indexed with a retryable OCR failure.
- **Pending:** the embedding's model is missing, or the video can't be sampled because FFmpeg is missing (the embedding is Failed with `FrameSamplingFfmpegUnavailable`, retryable, and the work item still completes).
- **Error (`analysis.embedding`):** the embedding failed for good, including a video whose stream can't be read. The scheduler marks it through `IFailureAwareStage.OnFailedAsync`, so a transient failure that's still being retried doesn't flip the item to Error.
- A run without the embedding, e.g. a retry of only OCR, leaves the status alone.

## Failures, retries and the circuit breaker (E05-S04)

The stage stores every analyzer's result before it fails the work item (decision "rethrow after storing"). It rethrows one held failure: the embedding's first, then a transient one, because that's the one a retry can fix.

- **Retry:** the scheduler re-runs the same work item. The stage skips the analyzers that already finished in that work item (their state is newer than the work item and Done, NotApplicable, or Failed for good), so only the failed ones run again. This also covers re-queues for a missing model and restarts.
- **Given up:** `OnFailedAsync` turns the item's Retrying states into Failed (not retryable if the error is permanent), and marks the item Error only for an embedding failure or a failure of the whole item (e.g. an unreadable file).
- **Circuit breaker:** before calling a describer or OCR provider, the stage checks `IProviderCircuitBreaker.IsOpen`. If the circuit is open, the analyzer is Skipped `ProviderUnavailable`, the others run, and then the work item fails with `AiFailureKind.Unreachable`. The scheduler parks it without using a retry until the circuit closes, so an LM Studio outage doesn't stop embeddings and leaves no failed items. A successful call calls `RecordSuccess`. The embedding is a local model and doesn't check the breaker.
- **Progress:** work waiting for a retry should show as "retrying", not as an error, so an Indexed item never sits next to an error count while its retry is pending.

## Re-indexing (E06-S07)

- **Outdated (T01):** `CurrentProvenance.Resolve(profile, registry)` gives, per enabled analyzer, the provider the next analysis would pick (`ProviderSelection`, ignoring the circuit breaker), its model and the prompt version it stores (the describer's; none for OCR and the embedding). `IAnalyzerStateRepository.GetOutdatedAsync` compares: the describer and OCR by their active run (provider ID, model name, prompt version; the model version only when both sides have one), the embedding by its vectors (an item with vectors, none from the current model). Disabled analyzers, analyzers without a usable provider, items without results and missing items are never outdated. `GetOutdatedAnalysisQuery(profile)` takes a proposed profile to count what a change would make outdated.
- **Command (T02):** `AnalyzeScopeCommand` with `AnalysisScope.Outdated`, `AnalysisScope.Library` or `Items` (the selection), optionally with analyzers. The outdated scope gives each work item a payload of only its outdated analyzers (`AnalyzeItemsCommand.ItemAnalyzers`), so a describer change doesn't re-embed or re-read text; with analyzers chosen, only items outdated in one of them, each running its outdated ones among them.
- **Activation (T03):** the unit is one analyzer's run. `ApplyRunAsync` writes the run (now active; the previous one is kept per `RetainedPreviousRuns`), its values and tags, the suggestion flags, the item's `search_text` columns and the analyzer state in one transaction, so an interrupted re-index leaves each analyzer of an item wholly old or wholly new. User values and tag rejections are never touched. An item can briefly have a new describer run and an old OCR run; the old one stays outdated, so re-indexing again finishes it. New embeddings are stored next to the old model's (`ReplaceAsync` replaces only one model's vectors).

## Pictures sent to providers

Analyzers get raw RGBA pixels (`AiImage.FromRgba`). OpenAI-compatible providers encode them as JPEG (quality 90) before sending; ONNX providers use the pixels directly.

## Limits

- The stage depends on the embedding model (`IModelDependentStage`), so it waits for that model even when the embedding is disabled in the profile.
- The embedding uses the first registered embedding provider, not the one the profile names.
- The profile's `PromptVersion` isn't used; the provider's prompt version is stored.
- A job cancelled while an analyzer is Retrying leaves that state as it is.
