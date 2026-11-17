# Search index (E01-S04)

The engine under search (E09) and filtering (E10): keyword search with FTS5, vector similarity in memory (D067), hybrid ranking, and SQL filters on every path. The UI and query parsing build on `ISearchIndex`.

## Pieces

| Piece | Project | What it does |
|---|---|---|
| `ISearchIndex`, `SearchQuery`, `SearchFilter`, `SearchResults` | `Infrastructure.Abstractions/Search` | The interface the use cases call. |
| `SearchIndex` | `Infrastructure.Search` | Follows the library session, loads the vectors, runs the modes and fuses the lists. Registered by `AddSearch()`. |
| `VectorIndex`, `ModelVectors` | `Infrastructure.Search/Vectors` | One contiguous half-precision array per model, scored with `TensorPrimitives.Dot`, split across cores above 8,192 vectors. |
| `ReciprocalRankFusion` | `Infrastructure.Search/Ranking` | `Σ weight / (k + rank)`; k = 10, weights 1 and 1, 200 candidates per list by default (`SearchRankingOptions`, tuned in E09-S05). |
| `ISearchStore` (`SearchStore`) | `Infrastructure.Abstractions/Search`, `DataAccess/Search` | The FTS5 index, filter and order SQL, matched fields, and the rebuild. `IOpenLibrary.Search`. |
| `IEmbeddingRepository` (`EmbeddingRepository`) | `Repositories`, `DataAccess/Repositories` | The only write path for embeddings; raises `Changed` after each commit. `IOpenLibrary.Embeddings`. |
| `RebuildSearchIndexCommand` | `UseCases/Search/RebuildSearchIndex` | "Rebuild or refresh the index" (spec 1.2). |

## Schema (version 6, prefixes changed in 11, sort keys added in 12, Russian stems added in 14)

The `SearchIndex` migration adds, in raw SQL:

- `search_document(doc_id INTEGER PRIMARY KEY AUTOINCREMENT, item_id TEXT UNIQUE)`: a stable integer per item, shared by the keyword and vector indexes. AUTOINCREMENT, so a deleted item's ID is never reused and a stale vector can't attach to a new item. `media_item.rowid` isn't used, because `VACUUM` (backups, D073) may renumber it.
- `search_text`: FTS5 with `unicode61 remove_diacritics 2` (D029) and prefix indexes for 1, 2 and 3 characters (1 since schema 11, MFC-988: the `SearchPrefixIndex` migration recreates the table and copies its rows, so no rebuild is needed). Columns: `title`, `description` (with the summary), `tags`, `ocr_text`, `transcript`, `other` (notes and the list fields), `file_name` (the path in the folder), and `stems` (schema 14, MFC-328, D349): the Snowball Russian stem of every word with a Cyrillic letter in the six metadata columns, in order (`RussianStems`, `Domain/Text/RussianStemmer`, a port checked against Snowball's own 49,785-word vocabulary). `stems` isn't a field: matched fields and BM25 weights cover the first seven columns (a missing weight is 1). The `RussianStems` migration recreates the table and copies the rows, computing the stems with `ru_stem`, a SQL function every library connection registers, so no rebuild is needed.
- `search_filename`: FTS5 `trigram` index of the path, for substring matches.
- `search_state`: `rebuild_needed` marks an index whose metadata columns must be recomputed. The migration sets it for libraries that have items; the app rebuilds in the background when it opens such a library.
- `search_sort(doc_id, name_key, item_date, size_bytes, imported_at)` (schema 12, MFC-762): one row per document with its sort keys, each indexed. `name_key` is the natural name key (`NaturalSortKey`): the file name upper-cased in every alphabet, with each run of digits written as its length (three digits) and the digits without leading zeros, so "IMG_2" sorts before "IMG_10" in plain binary order. It comes from `natural_key`, a SQL function every library connection registers (like `fold_case`), so a connection without it can't add items or rename them. `item_date` is the item date (below).
- Triggers on `media_item` insert, path update and delete keep documents, paths and the name key in step; on `media_item` file changes (`file_modified_at`, `size_bytes`, `imported_at`) and on `technical_metadata` insert, date change and delete they keep the other sort keys current. A rebuild rewrites every `search_sort` row.

`SearchSchema.cs` holds the same statements with `IF NOT EXISTS`, so a rebuild recreates dropped tables. A test compares the result with the migration's schema.

### Keeping it current

- **Metadata:** `ItemMetadataRepository` writes the item's `search_text` columns in the same transaction as the metadata, from `ItemMetadata.GetEffectiveValues()` and `GetEffectiveTags()`. The precedence rules (user, then AI, then file) stay in the domain. Any new write path for descriptive metadata must call `SearchStore.UpdateTextAsync`.
- **Paths, new and deleted items:** triggers.
- **Embeddings:** `EmbeddingRepository` raises `Changed` after the commit, with the vectors; `SearchIndex` applies them. While a load or rebuild reads the database, the commits are buffered and replayed onto the new index; replays are harmless because every change replaces all of an item's vectors of a model.
- `ё` is folded to `е` in indexed text, paths and query words, because `unicode61` removes only Latin diacritics.
- **Stems:** `SearchText` writes the `stems` column with the other columns, so every metadata write and the rebuild keep it current.

## Queries

- **Words:** split on white space; words without a letter or digit are dropped; at most 16. Each word is an FTS5 string, so punctuation is never query syntax. Every word must match as a prefix in any field, or as a path substring if it has 3+ characters.
- **Russian words (D349):** a word whose letters are all Cyrillic, with 3+ letters, whose stem differs from it, also matches its exact stem in `stems` (`SearchSql.MatchTerm`: `("собаки"* OR stems : "собак")`), so "собаки" finds "собакой" and "котами" finds "кота" but not "котёл". A prefix of the stem would add nothing, since a stem is always the start of its word. A word found only through its stem marks no matched field. English queries are unchanged.
- **Keyword ranking:** BM25 with every field weighted 1 by default (`KeywordFieldWeights`, tuned in E09-S05), over the documents whose words match every term; documents that matched a word only as a path substring rank after them.
- **Vector:** the filter's SQL returns document IDs, which become a mask the scorer checks. An item scores as its best vector. Excluded items are never in the mask.
- **Hybrid:** the top 200 of each list, fused by RRF. Ties go to the better single rank, then the keyword list, then the lower document ID.
- **Other orders** (name, date, size, import date; E10-S04) work in every mode: for vector and hybrid queries the ranked set (the 200 best candidates of each list, after the relevance cut) is sorted in SQL; keyword-only queries sort every match. Each order ends with the document ID in the same direction, so paging never shows an item twice or skips one. A list (no text, no vector) reads a page off the sort key's index: the `ORDER BY` names only `search_sort` columns (`s."doc_id"` as the tie-breaker, not `d."doc_id"`, which would make SQLite sort every row), and the total is counted separately, since a `count(*) OVER ()` window reads every row too. Natural directions: name A to Z, the others newest or largest first; `ReverseSort` flips them. `SortMode.Relevance` without text or vector lists the newest imports first.
- **Item date:** date taken, converted to UTC with its offset when known, else the file modification time.
- **Relevance cut (E09-S01-T03):** with `SearchQuery.RelevanceCut`, a hit that only the vector search found is "less relevant" unless its similarity minus the query's mean similarity over the library clears `MinCentredSimilarity`, or its raw similarity clears `MinSimilarity`. Less relevant hits are dropped after fusion (so the others keep their order) and counted in `SearchResults.LessRelevantCount`; `IncludeLessRelevant` returns them marked `SearchHit.IsLessRelevant`. The mean comes from a running sum of each item's first vector per model, kept in `ModelVectors`. Keyword matches are never cut.

## Registered filters (E10-S01)

A filter is an `IFilterDefinition` (`Infrastructure.Abstractions/Search/Filters`) registered as a singleton; derive from `FilterDefinition<TValue>`. It declares its ID, display name resource key, panel order, kind (multi-select, range, date range, boolean, include/exclude) and value type, whether it is available, and its SQL condition, which it writes through `IFilterSql` (`media_item` and `technical_metadata` columns, the item date, parameters, `LIKE`). `FilterRegistry` (UseCases) collects them. The active filters are a `FilterState` (filter ID → `FilterValue`), which `FilterStateSerializer` writes as `{"version":1,"filters":[{"id":"media-type","value":{"values":["Video"]}}]}`. `SearchMediaQuery.Filters` and `FindSimilarQuery.Filters` turn the state into `SearchFilter.Conditions`, which `SearchSql` ANDs into the same `WHERE` as the fixed filters, so every path applies them. Filters of different IDs combine with AND; values within one filter follow its kind (any of a multi-select). Unavailable filters don't apply. `SearchResultSource.SetFilters` applies them to every search and to browsing; with filters and no text, browsing goes through the search index, in the browse order (E10-S04).

### Core filters (E10-S03)

| ID | Order | Kind | Values |
|---|--:|---|---|
| `media-type` | 100 | multi-select | `MediaType` names: `Image`, `AnimatedImage`, `Video` |
| `file-format` | 200 | multi-select | format registry IDs (`jpeg`, `png`, `webp`, `gif`, `mp4`), matched by each format's extensions |
| `date` | 300 | date range | presets `today`, `last-7-days`, `last-30-days`, `this-year`, `last-year`, or a custom range |
| `resolution` | 400 | resolution | presets `small`, `hd`, `full-hd`, `4k`, or a minimum displayed width and height |
| `status` | 500 | multi-select | `pending` (discovered or queued), `indexed`, `error`, `missing` |
| `folder` | 600 | folder | indexed folders and subfolders (`<folder ID>` or `<folder ID>/<path>`), with or without their subfolders |
| `tags` | 700 | include/exclude | tags, all (AND) or any (OR) of the included ones and none of the excluded ones |

The date and resolution filters list their presets through `IFilterOptions` (value = preset ID, display name key `Filter_Date_*` / `Filter_Resolution_*`). A preset wins over custom values, and an unknown preset matches nothing. Fixed option lists (media type, file format, status, date and resolution presets) ignore the `IFilterOptions` prefix and always return every value, since the panel narrows them by the localised text it shows; the tag and folder lists use it.

- **Date** compares in the viewer's local calendar, from `TimeProvider`. A date taken with an unknown offset (D105) is a local clock time already and compares as it is; any other date (date taken with an offset, video creation time, file modification time, or `media_item.file_modified_at` when there is no technical metadata yet) is an instant and compares with the instants at which the range's local days start, found with the local time zone (a midnight skipped by daylight saving time maps to the change, a repeated one to its first occurrence). "Last 7 days" and "last 30 days" are calendar days with today, up to midnight tonight, so items dated in the future are left out. A custom range uses the clock times of `From` (inclusive) and `To` (exclusive), whatever offset they carry.
- **Resolution** uses the displayed dimensions (orientation 5 to 8 swap width and height). Presets compare the short and the long side, so portrait items count alike: `small` = long side under 800 px, `hd` ≥ 1280 × 720, `full-hd` ≥ 1920 × 1080, `4k` ≥ 3840 × 2160. Items with no dimensions yet never match.
- **Status** offers four values through `IFilterOptions` (`Filter_Status_*`); excluded items never reach the results, so there is no value for them. Unknown values are ignored.
- **Folder** uses `FolderValue(Folders, IncludeSubfolders)` and its own kind, `Folder`. Folder entries are kept with `/` separators and no empty segments, so one folder written two ways is one value. Subfolder names ignore case in every alphabet, as on Windows and in the folder tree: the paths go through `fold_case`, a SQL function every library connection registers (`ToUpperInvariant`), since SQLite's own case folding covers ASCII only. Without subfolders, an item must be directly in the folder: no `/` in its path, or none after the subfolder's. `IFilterOptions` gives the indexed folders by path for no prefix (Offline ones with `FilterOption.IsOffline`, D025: they can still be picked) and the subfolders of a folder or subfolder for its value as the prefix, from the items' paths like the folder tree. A value whose folder ID can't be read matches nothing.
- **Tags** match effective tags, as `ItemMetadata.GetEffectiveTags` resolves them (`EffectiveTagSql`): user tags; AI tags of a run that is active, completed and not rejected, unless the user rejected the tag on the item; technical tags unless rejected. Tags compare by `tag_key` (upper case, D071), after trimming and collapsing white space. `IFilterOptions` autocompletes from the effective tags starting with the typed text, most items first, at most 20 (`IItemMetadataRepository.GetEffectiveTagsAsync`).

## Unified text search (E09-S01)

`SearchMediaQuery` (MediatR, `UseCases/Search/SearchMedia`) is the call the search bar makes. It normalises the text (`SearchText`: NFC, invisible characters out, white space collapsed, 256 characters at most), encodes it with `SemanticQueryEncoder`, and runs one `ISearchIndex` query with both text and vector. Empty text browses by date taken, newest first, when the sort is Relevance.

## Sorting (E10-S04)

The results header ends with a sort dropdown (Relevance while searching, Name, Date taken, File size, Date imported) and a direction button, hidden for Relevance (`SortBarViewModel`, `SortBarView`; provisional until the T00 design). `SearchResultSource` keeps two orders: one while searching (text, a reference image or Find similar; Relevance by default) and one while browsing (date taken, newest first, by default), and `SetSort` changes the one in use. Picking a mode starts in its natural direction. Browsing in "Date imported, newest first" is the M1 item list (D177); any other browse order goes through the search index, without a search notice. `SortStatePersistence` keeps both orders in `settings.json` (`search.sort`, `browse.sort`, e.g. `Name asc`), for every library. With a query, a non-relevance order sorts only the results that pass the relevance cut. Select all covers the results up to the newest item in the scope at that moment (`IMediaResultSource.GetNewestIdAsync`), in any order: a result is covered if its UUIDv7 isn't newer, so items imported later are left out (`GetIdsAsync` with `newest`, `CountNewerAsync`).

Lists read a page off the sort key's index even with a filter or a folder scope (user choice). At 100k items, in an order that doesn't follow import order (size; name and date in real libraries) and with a filter, a 200-item page takes about 2 / 124 / 485 / 832 ms at offsets 0 / 10k / 40k / 70k, against 64-105 ms if SQLite sorted the matching rows; without a filter, 0-100 ms. The benchmark's "Sort paging" table pages through every item in every order and checks for gaps and duplicates.

Benchmark at 100k items (MFC-762, 28 logical CPUs; ms):

- **Paging**, pages of 1,000, every item in each order and direction: no gaps or duplicates. p50 / p95 per page without a filter: name 17 / 28, date 10-14 / 20, size 81 / 148, import date 16 / 26; with the "images" filter: name, date and import date 89-99 / 113-139, size 537-559 / 982-988. "list, by date" (100 items) is 5.6 ms p50, was 43 ms before the sort keys.
- **Search as you type sorted by name** (`--typing-sort Name`; the hybrid candidates are sorted in SQL): search p50 / p95 98 / 128 (1-2 letters), 125 / 474 (3-5), 203 / 436 (6+), 157 / 413 (all), no slower than relevance order in the same build (97 / 120, 130 / 519, 241 / 520, 186 / 501), which matches MFC-989.
- **Upgrade to schema 12:** the first open of the 100k library took 8.5 s (the sort keys are filled when it opens) against 1.9 s afterwards. If the model is missing, the vector index isn't ready or encoding fails, the keyword results come back alone and `MediaSearchResults.Semantic` says why. The relevance cut per model is in `SemanticRelevanceThresholds`; a model without an entry shows every neighbour.

## Search as you type (E09-S02)

`SearchBarViewModel` searches 200 ms after the last keystroke (`DefaultDelay`; 300 ms until MFC-990), `SearchResultSource` cancels the search before it, and a search that was replaced never hands its answer on, even when it had finished. Two caches make going back instant: `SemanticQueryEncoder` keeps 256 query vectors, and `SearchPageCache` keeps the first page of the last 32 text searches (64 cached answers) (reset when the scope or items change; answers that lack the semantic search for a reason that can pass are never kept). The time from the last keystroke to the results is logged (`Search as you type: N ms ...`).

`production` also searches every prefix of the keyword queries (hybrid, no filter, limit 100). MFC-91 searched each prefix once: on 100k items and 28 CPUs, p50 221 ms, p95 3.9 s, max 7.8 s (1-2 letters p95 5.1 s, 3-5 letters 0.8 s, 6+ letters 2.0 s), without the encoder and the pause.

MFC-991 repeats it: one warm-up pass, then 3 runs of each of the 235 prefixes (705 samples; `--typing-runs` changes the 3). It also times the real SigLIP 2 text encoder (uncached) on the same prefixes, and adds the pause arithmetically (nothing sleeps; 300 ms until MFC-990, 200 ms since). The library holds synthetic vectors, so the encoder's output is only timed, not searched with. **Baseline for MFC-988, 989 and 990** (100k items, 28 logical CPUs, Windows 11, .NET 10.0.15, DirectML build of ONNX Runtime; ms):

| Letters typed | Samples | Search p50 | Search p95 | Search max | Encoder p50 | Encoder p95 | End to end p50 | End to end p95 | End to end max |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 1 to 2 | 150 | 242 | 3,732 | 4,842 | 22 | 37 | 579 | 4,054 | 5,164 |
| 3 to 5 | 213 | 196 | 762 | 3,150 | 22 | 25 | 517 | 1,091 | 3,471 |
| 6 or more | 342 | 271 | 1,903 | 4,936 | 22 | 35 | 597 | 2,223 | 5,259 |
| all | 705 | 241 | 3,165 | 4,936 | 22 | 35 | 567 | 3,484 | 5,259 |

The D031 budget of 300 ms is **not met**, and the encoder is not the cause (about 22 ms, within its 50 ms budget). The 300 ms pause alone uses the whole budget if it is counted in it, so end to end (p50 567 ms) can only meet it if the pause is shortened or left out of the target (MFC-990); the search alone has a p50 of 241 ms but a p95 of 3.2 s. The tail is the same as in MFC-91: short prefixes match most of the library. Another full run gave a search p50 of 175 ms for all prefixes and p95 of 3.4 s, so p50 moves by about 50 ms between runs on this machine while p95 stays at 3-4 s; compare p95 and the buckets, and rerun before judging a change of less than 25%.

### MFC-988: one-character prefixes

The tail was not the search SQL (at most about 370 ms for any prefix) but the matched fields of the first page (`GetMatchedFieldsAsync`). It runs one FTS5 lookup per result row, and a one-character prefix had no prefix index, so each lookup read every token starting with that letter: about 32 ms per row, 3-4 s for 100 rows ("b", and the last word of "train s" or "red c", which is why the 6+ bucket had the tail too). Two- and three-character prefixes took about 1 ms for all 100 rows. The 1-character prefix index (schema 11) brings the lookup to about 2 ms and makes the index about 22% larger (92 MB to 112 MB of FTS data at 100k items). Upgrading copies the index rows when the library opens: about 7 s at 100k items, once.

Same benchmark after MFC-988 (ms):

| Letters typed | Samples | Search p50 | Search p95 | Search max | Encoder p50 | Encoder p95 | End to end p50 | End to end p95 | End to end max |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 1 to 2 | 150 | 206 | 282 | 322 | 23 | 29 | 530 | 604 | 644 |
| 3 to 5 | 213 | 195 | 539 | 819 | 23 | 30 | 519 | 861 | 1,149 |
| 6 or more | 342 | 255 | 529 | 754 | 23 | 29 | 576 | 854 | 1,077 |
| all | 705 | 221 | 484 | 819 | 23 | 29 | 545 | 805 | 1,149 |

The rerun of the baseline before the change gave a search p95 of 3.9 s (1-2 letters 5.0 s), so the p95 fell about 8 times. The user confirmed that D031's 300 ms is counted from the end of the typing pause (encoder and search), not from the last keystroke.

### MFC-989: top matches for the hybrid candidates

The keyword half of a hybrid search needs only the 200 best BM25 matches (`SearchRankingOptions.CandidateLimit`) and never their total, so it asks with `KeywordSearch.NeedsTotal = false`. When the first page is asked for by relevance, `SearchStore` then runs just the FTS5 match with BM25, the filter and `ORDER BY rank LIMIT n`, which SQLite sorts keeping only the top rows. A full page is exactly the page the full query gives, since hits found only as a path substring rank after every BM25 match. A shorter page falls back to the full query (intersection, path substrings, count). The keyword-only path keeps its exact total. Measured on the SQL alone: 50-180 ms down to 12-61 ms for broad prefixes.

Same benchmark after MFC-989 (ms; a first run while other builds were busy gave an encoder p50 of 231 ms and was discarded):

| Letters typed | Samples | Search p50 | Search p95 | Search max | Encoder p50 | Encoder p95 | End to end p50 | End to end p95 | End to end max |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 1 to 2 | 150 | 96 | 118 | 127 | 23 | 24 | 419 | 441 | 450 |
| 3 to 5 | 213 | 131 | 512 | 735 | 23 | 25 | 456 | 836 | 1,061 |
| 6 or more | 342 | 233 | 514 | 710 | 23 | 25 | 557 | 837 | 1,034 |
| all | 705 | 179 | 497 | 735 | 23 | 25 | 503 | 820 | 1,061 |

Broad prefixes (1-2 letters) are now within D031 (encoder + search p95 about 142 ms). The p95 of longer prefixes stays at about 500 ms: the matched fields of the first page cost 60-220 ms per 100 rows for prefixes of 4 or more letters, which have no prefix index, plus the vector half. The user chose to leave that as it is for now (other options were working the fields out in C# from the row text, or more prefix indexes).

### MFC-990: typing pause of 200 ms

D031's 300 ms is counted from the end of the pause, so the pause is chosen for how fast results feel against how many searches it starts. The benchmark simulates typing each query 200 times with log-normal gaps between keystrokes (sigma 0.5) for a fast typist (median gap 150 ms) and a slower one (250 ms), using the measured encoder + search time of each prefix (`TypingSimulation`). A search starts after a gap longer than the pause and after the last keystroke, and it is cancelled when the next one starts before it is done (typing alone doesn't cancel it, as in the app). Searches started and cancelled per typed query:

| Pause | Fast typist: started | cancelled | Slower typist: started | cancelled |
|--:|--:|--:|--:|--:|
| 150 ms | 5.2 | 2.2 | 8.1 | 3.5 |
| 200 ms | 3.4 | 1.0 | 6.6 | 2.4 |
| 250 ms | 2.3 | 0.4 | 5.2 | 1.5 |
| 300 ms | 1.7 | 0.2 | 4.0 | 0.9 |

The results arrive the pause plus the encoder and search after the last keystroke, so every 50 ms off the pause shows them 50 ms sooner. The user chose 200 ms: 100 ms sooner than before, at about one cancelled search per query for a fast typist and 2.4 for a slower one, which the cheaper searches after MFC-988 and 989 allow. The started counts don't depend on the machine's load, but the cancelled counts do: a final run while other builds were busy (encoder p95 345 ms instead of 25 ms) gave 0.76 and 1.96 cancelled per query at 200 ms, and search p95 numbers that aren't comparable, so the clean MFC-989 table above stays the reference for search times (the pause adds a constant). The simulated gaps are a model, not recorded typing; the app logs the time from the last keystroke to the results, so real numbers can check it.

## Relevance tuning (E09-S05)

The ranking settings are code defaults; the evaluation tool's flags override them for a run, and the app has no setting for them.

| Setting | Where | Default | Flag |
|---|---|---|---|
| RRF k | `SearchRankingOptions.RrfK` | 10 (was 60) | `--rrf-k` |
| Keyword and vector list weights | `SearchRankingOptions.KeywordWeight`, `VectorWeight` | 1 and 1 | `--keyword-weight`, `--vector-weight` |
| Candidates per list | `SearchRankingOptions.CandidateLimit` | 200 | none (changing it changes search cost) |
| BM25 field weights | `KeywordFieldWeights` | 1 for every field (was title 4, tags 3, file name 2, others 1) | `--field-weights title=4,tags=3,...` |
| Semantic relevance cut | `SemanticRelevanceThresholds.Siglip2Base` | centred 0.07, raw 0.13 | `--threshold centred,raw` or `none` |

How they were picked: the corpus (351 items, 139 English and 142 Russian queries, 20 of them that should find nothing) was described and read once by the app's own describer and OCR with `qwen/qwen3-vl-4b` (`eval/analysis/`), then indexed by the app's `SearchIndex` (`app-hybrid`, `app-vector`, `app-keyword`; see `docs/evaluation-corpus.md`). The untuned run is pinned in `eval/baselines.json` `references`. `tune` scored RRF k 10/30/60 × keyword weight 0.5/1/1.5/2 × four field-weight sets × three thresholds (`eval/reports/2026-11-14_163213_tune-app-hybrid.md`).

| Run | Recall@10 en | Recall@10 ru | MRR en | MRR ru | Nonsense queries with results en / ru |
|---|--:|--:|--:|--:|--:|
| Untuned hybrid (baseline run) | 0.9288 | 0.7407 | 0.9638 | 0.8040 | 0.1 / 0.9 |
| **Tuned hybrid (k 10, flat field weights)** | **0.9304** | **0.7407** | **0.9716** | **0.8040** | 0.1 / 0.9 |
| Vector only, same threshold | 0.7905 | 0.6726 | 0.8682 | 0.7352 | 0.1 / 0.9 |
| Keyword only | 0.7160 | 0.1787 | 0.8220 | 0.2311 | 0 / 0 |
| Hybrid, threshold 0.05 / 0.12 | 0.9682 | 0.8026 | 0.9774 | 0.8353 | 0.6 / 1.0 |
| Hybrid, threshold 0.09 / 0.15 | 0.8812 | 0.5800 | 0.9561 | 0.6962 | 0 / 0 |

- The ranking settings move the numbers little: across the whole grid at the current threshold, recall@10 stays within 0.925 to 0.932 (en) and doesn't change (ru). The tuned row is the only one that is better or equal on every metric; keyword weight 0.5 gives the best English MRR (0.9755) but costs Russian MRR (0.7907).
- The threshold decides most of it. Looser cuts find more (+0.04 en, +0.06 ru) but then most nonsense queries show results; stricter cuts hide nonsense entirely but lose a fifth of the Russian recall. The cut stays where E09-S01 put it.
- Hybrid beats vector-only by 0.14 (en) and 0.07 (ru) recall@10. Russian stays lower because the descriptions and tags are in English (the profile language), so a Russian query finds them only through SigLIP 2; abstract and tag-style Russian queries are the weakest categories.
- Flat field weights mean a word in the title no longer outranks the same word as a prefix in a shorter file name; on the corpus that cost nothing (the filename and tag categories keep their scores).
- The headline "below" status in these reports compares with `baselines.json`'s minimums, which were set for the `siglip2` system on the content categories only; the app systems also score the filename, tag and none categories.

## Known follow-ups

- A text or image search that ran while the vector index was still loading (`SemanticSearchStatus.IndexNotReady`) is not run again when the index finishes loading; the user has to search again (found in PR #58, MFC-93). Such answers are not cached.

## Benchmark

`dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks -- production` copies a real library (`C:\gb2\data\libs\Bench.mfc` by default; `--source`), gives every item synthetic metadata and three 768-d vectors through the repositories, and measures the load, the rebuild and every query mode under three filters. `--cpus 4` runs it on four logical CPUs. Results are in the MFC-89 pull request.

At 100k items every mode is within the 200 ms budget (D031) except keyword and hybrid queries whose words each match a large share of the library: they build, count and order the whole hit set, about 190 ms p50 and 330–450 ms p95 on both 28 and 4 CPUs. That is an accepted exception; a top-N fast path for relevance order is the way to lift it if it shows in use.
