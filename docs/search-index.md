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

## Unified text search (E09-S01)

`SearchMediaQuery` (MediatR, `UseCases/Search/SearchMedia`) is the call the search bar makes. It normalises the text (`SearchText`: NFC, invisible characters out, white space collapsed, 256 characters at most), encodes it with `SemanticQueryEncoder`, and runs one `ISearchIndex` query with both text and vector. Empty text browses by date taken, newest first. If the model is missing, the vector index isn't ready or encoding fails, the keyword results come back alone and `MediaSearchResults.Semantic` says why. The relevance cut per model is in `SemanticRelevanceThresholds`; a model without an entry shows every neighbour.

## Search as you type (E09-S02)

`SearchBarViewModel` searches 300 ms after the last keystroke (`DefaultDelay`), `SearchResultSource` cancels the search before it, and a search that was replaced never hands its answer on, even when it had finished. Two caches make going back instant: `SemanticQueryEncoder` keeps 256 query vectors, and `SearchPageCache` keeps the first page of the last 32 text searches (reset when the scope or items change; answers that lack the semantic search for a reason that can pass are never kept). The time from the last keystroke to the results is logged (`Search as you type: N ms ...`).

`production` also searches every prefix of the keyword queries (hybrid, no filter). On 100k items and 28 CPUs: p50 221 ms, p95 3.9 s, max 7.8 s (1-2 letters p95 5.1 s, 3-5 letters 0.8 s, 6+ letters 2.0 s), without the encoder and the pause. The D031 budget of 300 ms is **not met**: short prefixes match most of the library because every word is a prefix (D221 and MFC-89's decision). Bottlenecks are filed as tasks.

## Known follow-ups

- A text or image search that ran while the vector index was still loading (`SemanticSearchStatus.IndexNotReady`) is not run again when the index finishes loading; the user has to search again (found in PR #58, MFC-93). Such answers are not cached.

## Benchmark

`dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks -- production` copies a real library (`C:\gb2\data\libs\Bench.mfc` by default; `--source`), gives every item synthetic metadata and three 768-d vectors through the repositories, and measures the load, the rebuild and every query mode under three filters. `--cpus 4` runs it on four logical CPUs. Results are in the MFC-89 pull request.

At 100k items every mode is within the 200 ms budget (D031) except keyword and hybrid queries whose words each match a large share of the library: they build, count and order the whole hit set, about 190 ms p50 and 330–450 ms p95 on both 28 and 4 CPUs. That is an accepted exception; a top-N fast path for relevance order is the way to lift it if it shows in use.
