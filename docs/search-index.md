# Search index (E01-S04)

The engine under search (E09) and filtering (E10): keyword search with FTS5, vector similarity in memory (D067), hybrid ranking, and SQL filters on every path. The UI and query parsing build on `ISearchIndex`.

## Pieces

| Piece | Project | What it does |
|---|---|---|
| `ISearchIndex`, `SearchQuery`, `SearchFilter`, `SearchResults` | `Infrastructure.Abstractions/Search` | The interface the use cases call. |
| `SearchIndex` | `Infrastructure.Search` | Follows the library session, loads the vectors, runs the modes and fuses the lists. Registered by `AddSearch()`. |
| `VectorIndex`, `ModelVectors` | `Infrastructure.Search/Vectors` | One contiguous half-precision array per model, scored with `TensorPrimitives.Dot`, split across cores above 8,192 vectors. |
| `ReciprocalRankFusion` | `Infrastructure.Search/Ranking` | `Σ weight / (k + rank)`; k = 60, weights 1 and 1, 200 candidates per list by default (`SearchRankingOptions`). |
| `ISearchStore` (`SearchStore`) | `Infrastructure.Abstractions/Search`, `DataAccess/Search` | The FTS5 index, filter and order SQL, matched fields, and the rebuild. `IOpenLibrary.Search`. |
| `IEmbeddingRepository` (`EmbeddingRepository`) | `Repositories`, `DataAccess/Repositories` | The only write path for embeddings; raises `Changed` after each commit. `IOpenLibrary.Embeddings`. |
| `RebuildSearchIndexCommand` | `UseCases/Search/RebuildSearchIndex` | "Rebuild or refresh the index" (spec 1.2). |

## Schema (version 6)

The `SearchIndex` migration adds, in raw SQL:

- `search_document(doc_id INTEGER PRIMARY KEY AUTOINCREMENT, item_id TEXT UNIQUE)`: a stable integer per item, shared by the keyword and vector indexes. AUTOINCREMENT, so a deleted item's ID is never reused and a stale vector can't attach to a new item. `media_item.rowid` isn't used, because `VACUUM` (backups, D073) may renumber it.
- `search_text`: FTS5 with `unicode61 remove_diacritics 2` (D029) and prefix indexes for 2 and 3 characters. Columns: `title`, `description` (with the summary), `tags`, `ocr_text`, `transcript`, `other` (notes and the list fields), `file_name` (the path in the folder).
- `search_filename`: FTS5 `trigram` index of the path, for substring matches.
- `search_state`: `rebuild_needed` marks an index whose metadata columns must be recomputed. The migration sets it for libraries that have items; the app rebuilds in the background when it opens such a library.
- Triggers on `media_item` insert, path update and delete keep documents and paths in step.

`SearchSchema.cs` holds the same statements with `IF NOT EXISTS`, so a rebuild recreates dropped tables. A test compares the result with the migration's schema.

### Keeping it current

- **Metadata:** `ItemMetadataRepository` writes the item's `search_text` columns in the same transaction as the metadata, from `ItemMetadata.GetEffectiveValues()` and `GetEffectiveTags()`. The precedence rules (user, then AI, then file) stay in the domain. Any new write path for descriptive metadata must call `SearchStore.UpdateTextAsync`.
- **Paths, new and deleted items:** triggers.
- **Embeddings:** `EmbeddingRepository` raises `Changed` after the commit, with the vectors; `SearchIndex` applies them. While a load or rebuild reads the database, the commits are buffered and replayed onto the new index; replays are harmless because every change replaces all of an item's vectors of a model.
- `ё` is folded to `е` in indexed text, paths and query words, because `unicode61` removes only Latin diacritics.

## Queries

- **Words:** split on white space; words without a letter or digit are dropped; at most 16. Each word is an FTS5 string, so punctuation is never query syntax. Every word must match as a prefix in any field, or as a path substring if it has 3+ characters.
- **Keyword ranking:** BM25 with field weights title 4, tags 3, file name 2, others 1, over the documents whose words match every term; documents that matched a word only as a path substring rank after them.
- **Vector:** the filter's SQL returns document IDs, which become a mask the scorer checks. An item scores as its best vector. Excluded items are never in the mask.
- **Hybrid:** the top 200 of each list, fused by RRF. Ties go to the better single rank, then the keyword list, then the lower document ID.
- **Other orders** (name, date, size, import date) work in every mode: for vector and hybrid queries the ranked set is sorted in SQL.
- **Item date:** date taken, converted to UTC with its offset when known, else the file modification time.
- **Relevance cut (E09-S01-T03):** with `SearchQuery.RelevanceCut`, a hit that only the vector search found is "less relevant" unless its similarity minus the query's mean similarity over the library clears `MinCentredSimilarity`, or its raw similarity clears `MinSimilarity`. Less relevant hits are dropped after fusion (so the others keep their order) and counted in `SearchResults.LessRelevantCount`; `IncludeLessRelevant` returns them marked `SearchHit.IsLessRelevant`. The mean comes from a running sum of each item's first vector per model, kept in `ModelVectors`. Keyword matches are never cut.

## Registered filters (E10-S01)

A filter is an `IFilterDefinition` (`Infrastructure.Abstractions/Search/Filters`) registered as a singleton; derive from `FilterDefinition<TValue>`. It declares its ID, display name resource key, panel order, kind (multi-select, range, date range, boolean, include/exclude) and value type, whether it is available, and its SQL condition, which it writes through `IFilterSql` (`media_item` and `technical_metadata` columns, the item date, parameters, `LIKE`). `FilterRegistry` (UseCases) collects them. The active filters are a `FilterState` (filter ID → `FilterValue`), which `FilterStateSerializer` writes as `{"version":1,"filters":[{"id":"media-type","value":{"values":["Video"]}}]}`. `SearchMediaQuery.Filters` and `FindSimilarQuery.Filters` turn the state into `SearchFilter.Conditions`, which `SearchSql` ANDs into the same `WHERE` as the fixed filters, so every path applies them. Filters of different IDs combine with AND; values within one filter follow its kind (any of a multi-select). Unavailable filters don't apply. `SearchResultSource.SetFilters` applies them to every search and to browsing; with filters and no text, browsing goes through the search index, newest import first.

### Core filters (E10-S03)

| ID | Order | Kind | Values |
|---|--:|---|---|
| `media-type` | 100 | multi-select | `MediaType` names: `Image`, `AnimatedImage`, `Video` |
| `file-format` | 200 | multi-select | format registry IDs (`jpeg`, `png`, `webp`, `gif`, `mp4`), matched by each format's extensions |
| `date` | 300 | date range | presets `today`, `last-7-days`, `last-30-days`, `this-year`, `last-year`, or a custom range |
| `resolution` | 400 | resolution | presets `small`, `hd`, `full-hd`, `4k`, or a minimum displayed width and height |

The date and resolution filters list their presets through `IFilterOptions` (value = preset ID, display name key `Filter_Date_*` / `Filter_Resolution_*`). A preset wins over custom values, and an unknown preset matches nothing.

- **Date** compares in the viewer's local calendar, from `TimeProvider`. A date taken with an unknown offset (D105) is a local clock time already and compares as it is; any other date (date taken with an offset, video creation time, file modification time, or `media_item.file_modified_at` when there is no technical metadata yet) is an instant and compares with the instants at which the range's local days start, found with the local time zone (a midnight skipped by daylight saving time maps to the change, a repeated one to its first occurrence). "Last 7 days" and "last 30 days" are calendar days with today, up to midnight tonight, so items dated in the future are left out. A custom range uses the clock times of `From` (inclusive) and `To` (exclusive), whatever offset they carry.
- **Resolution** uses the displayed dimensions (orientation 5 to 8 swap width and height). Presets compare the short and the long side, so portrait items count alike: `small` = long side under 800 px, `hd` ≥ 1280 × 720, `full-hd` ≥ 1920 × 1080, `4k` ≥ 3840 × 2160. Items with no dimensions yet never match.

## Unified text search (E09-S01)

`SearchMediaQuery` (MediatR, `UseCases/Search/SearchMedia`) is the call the search bar makes. It normalises the text (`SearchText`: NFC, invisible characters out, white space collapsed, 256 characters at most), encodes it with `SemanticQueryEncoder`, and runs one `ISearchIndex` query with both text and vector. Empty text browses by date taken, newest first. If the model is missing, the vector index isn't ready or encoding fails, the keyword results come back alone and `MediaSearchResults.Semantic` says why. The relevance cut per model is in `SemanticRelevanceThresholds`; a model without an entry shows every neighbour.

## Search as you type (E09-S02)

`SearchBarViewModel` searches 300 ms after the last keystroke (`DefaultDelay`), `SearchResultSource` cancels the search before it, and a search that was replaced never hands its answer on, even when it had finished. Two caches make going back instant: `SemanticQueryEncoder` keeps 256 query vectors, and `SearchPageCache` keeps the first page of the last 32 text searches (64 cached answers) (reset when the scope or items change; answers that lack the semantic search for a reason that can pass are never kept). The time from the last keystroke to the results is logged (`Search as you type: N ms ...`).

`production` also searches every prefix of the keyword queries (hybrid, no filter, limit 100). MFC-91 searched each prefix once: on 100k items and 28 CPUs, p50 221 ms, p95 3.9 s, max 7.8 s (1-2 letters p95 5.1 s, 3-5 letters 0.8 s, 6+ letters 2.0 s), without the encoder and the pause.

MFC-991 repeats it: one warm-up pass, then 3 runs of each of the 235 prefixes (705 samples; `--typing-runs` changes the 3). It also times the real SigLIP 2 text encoder (uncached) on the same prefixes, and adds the 300 ms pause arithmetically (nothing sleeps). The library holds synthetic vectors, so the encoder's output is only timed, not searched with. **Baseline for MFC-988, 989 and 990** (100k items, 28 logical CPUs, Windows 11, .NET 10.0.15, DirectML build of ONNX Runtime; ms):

| Letters typed | Samples | Search p50 | Search p95 | Search max | Encoder p50 | Encoder p95 | End to end p50 | End to end p95 | End to end max |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 1 to 2 | 150 | 242 | 3,732 | 4,842 | 22 | 37 | 579 | 4,054 | 5,164 |
| 3 to 5 | 213 | 196 | 762 | 3,150 | 22 | 25 | 517 | 1,091 | 3,471 |
| 6 or more | 342 | 271 | 1,903 | 4,936 | 22 | 35 | 597 | 2,223 | 5,259 |
| all | 705 | 241 | 3,165 | 4,936 | 22 | 35 | 567 | 3,484 | 5,259 |

The D031 budget of 300 ms is **not met**, and the encoder is not the cause (about 22 ms, within its 50 ms budget). The 300 ms pause alone uses the whole budget if it is counted in it, so end to end (p50 567 ms) can only meet it if the pause is shortened or left out of the target (MFC-990); the search alone has a p50 of 241 ms but a p95 of 3.2 s. The tail is the same as in MFC-91: short prefixes match most of the library. Another full run gave a search p50 of 175 ms for all prefixes and p95 of 3.4 s, so p50 moves by about 50 ms between runs on this machine while p95 stays at 3-4 s; compare p95 and the buckets, and rerun before judging a change of less than 25%.

## Known follow-ups

- A text or image search that ran while the vector index was still loading (`SemanticSearchStatus.IndexNotReady`) is not run again when the index finishes loading; the user has to search again (found in PR #58, MFC-93). Such answers are not cached.

## Benchmark

`dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks -- production` copies a real library (`C:\gb2\data\libs\Bench.mfc` by default; `--source`), gives every item synthetic metadata and three 768-d vectors through the repositories, and measures the load, the rebuild and every query mode under three filters. `--cpus 4` runs it on four logical CPUs. Results are in the MFC-89 pull request.

At 100k items every mode is within the 200 ms budget (D031) except keyword and hybrid queries whose words each match a large share of the library: they build, count and order the whole hit set, about 190 ms p50 and 330–450 ms p95 on both 28 and 4 CPUs. That is an accepted exception; a top-N fast path for relevance order is the way to lift it if it shows in use.
