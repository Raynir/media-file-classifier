# Spike: vector search in SQLite (MFC-17, E01-SP01)

**Resolves:** Q5. **Decision:** D067 (draft at the end). **Status:** measured on desktop. Android numbers are deferred to MFC-18 (E01-SP02).

## Question

How should embedding similarity search work on top of SQLite, on desktop and on Android? The spike compared:

- **A. sqlite-vec:** vector search inside SQL, next to the filters.
- **B. BLOBs searched in memory:** embeddings stored as BLOBs in SQLite, loaded into one array, and scored with `System.Numerics.Tensors`.
- **C. An in-process ANN library (USearch etc.):** only worth it if B is too slow.

## Recommendation

**Use B: embeddings as BLOBs in SQLite, searched brute force in memory.** Don't take sqlite-vec, and don't add an ANN library (C).

- **B is 10 to 70 times faster than A on the same data.** At 100k items × 3 vectors (300k vectors), B queries in 6 to 45 ms. sqlite-vec takes 170 to 900 ms, and even tuned it misses the 200 ms budget (D031) on this fast machine before FTS and hybrid ranking are added.
- **B needs no native dependency.** sqlite-vec has no stable NuGet package. The official one is `0.1.7-alpha.2.1` and has no Android or win-arm64 binaries. Android would need hand-packed `.so` files for every ABI, and loading them is still unproven on the phone.
- **Filters are easy in B.** The filter runs as ordinary SQL (any join or condition), and the matching ids become a bitmask for the scorer. The narrower the filter, the faster the search.
- **The costs of B are memory and load time, and both are manageable.** Half precision halves the memory (300 MB for 300k vectors) with a recall@20 of 0.9998. Loading takes 1.4 to 2 s for 300k vectors on desktop, so it runs in the background at startup.
- **Consistency comes from discipline in B, not for free.** SQLite stays the source of truth, and the in-memory index is a cache that is updated after each commit. E01-S04 owns that rule (see below).

The details for E01-S04 and MFC-19 are in the [D067 draft](#d067-draft).

## How it was measured

The harness is [`benchmarks/MediaFileClassifier.Benchmarks`](../../benchmarks/MediaFileClassifier.Benchmarks/README.md). The whole matrix is one command, run from the repository root:

```powershell
dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks
```

`-- check` runs the yes/no checks, and `-- --quick` runs a one-minute smoke test.

- **Libraries:** 10k and 100k items, each with 1 vector per item and with 3 per item. The 3× case is the budget for E06-S02 keyframes and E06-S07 model migration, so the largest case is 300k vectors. Items have realistic text: a Windows path, a 150 to 600 character description, tags, and OCR text on 15% of them. Embeddings are 512-dim, L2-normalized, and clustered around 256 concepts, so ranking and recall mean something.
- **Queries:** the same 100 stored query vectors for every engine, asking for the top 20 items. Each runs under three filters: none; broad (library 1, photos since 2020, about 16% of items); narrow (library 2, videos, about 6%). An item with several vectors scores as its best vector.
- **Isolation:** each engine and library pair runs in its own process, after a JIT warm-up.
- **Correctness:** recall is measured against the exact float32 result. `check` covers extension loading, filters and transactions (27 checks, all pass).
- **Machine:** Intel Core i7-14700HX (20 cores, 28 threads), 32 GB RAM, NVMe SSD, Windows 11, .NET 10.0.14, SQLite 3.53.3 (SQLitePCLRaw `bundle_e_sqlite3` 2.1.13, through Microsoft.Data.Sqlite 10.0.14), sqlite-vec 0.1.7-alpha.2.1. This is faster than the D031 CPU tier (4 to 8 cores). Treat the single-threaded rows as the guide for that tier, and expect them to be about 1.5 to 2 times slower there.

### Engines

| Engine | Option | What it does |
|---|---|---|
| `memory-f32-cosine` | B | float32 array, `TensorPrimitives.CosineSimilarity`. The exact reference. |
| `memory-f32-dot` | B | float32, `TensorPrimitives.Dot`. Equal to cosine because the vectors are normalized. |
| `memory-f16-dot` | B | Half precision storage, widened to float32 per vector (`ConvertToSingle`), then `Dot`. |
| `memory-f16-cosine-generic` | B | Half precision with the generic `TensorPrimitives.CosineSimilarity<Half>`. |
| `…-parallel` | B | The same, split across cores (above 8k vectors). |
| `sqlitevec-knn` | A | KNN on a `vec0` table. The filters are vec0 metadata columns. |
| `sqlitevec-scan` | A | `vec_distance_cosine()` over the BLOB table. The filters are ordinary SQL. |
| `sqlitevec-*-tuned` | A | The same with `mmap_size` 4 GB and a 1 GB page cache, instead of the 2 MB default. |

## Results (desktop)

Latency is in ms, p50 / p95. Memory is the growth of the process (private bytes) over its baseline, in MB.

### 100k items × 3 vectors = 300k vectors (the sizing case)

| Engine | Load | First query | No filter | Broad | Narrow | Memory | Recall@20 |
|---|--:|--:|--:|--:|--:|--:|--:|
| memory-f32-cosine | 1,424 | 64 | 33.2 / 44.3 | 8.6 / 9.4 | 3.9 / 4.4 | 635 | 1.0000 |
| memory-f32-dot | 1,501 | 220 | 24.0 / 27.8 | 8.3 / 9.1 | 3.5 / 4.0 | 635 | 1.0000 |
| memory-f32-dot-parallel | 1,379 | 72 | 9.1 / 10.6 | 5.1 / 7.0 | 2.0 / 2.9 | 637 | 1.0000 |
| memory-f16-dot | 1,696 | 265 | 44.8 / 60.1 | 13.4 / 17.8 | 5.2 / 8.8 | 335 | 0.9998 |
| memory-f16-dot-parallel | 2,005 | 286 | 6.0 / 9.5 | 5.2 / 6.7 | 2.0 / 3.1 | 339 | 0.9998 |
| memory-f16-cosine-generic | 1,905 | 316 | 82.2 / 97.9 | 15.4 / 19.8 | 6.7 / 10.9 | 330 | 0.9950 |
| sqlitevec-knn | 21 | 472 | 446 / 518 | 376 / 447 | 377 / 443 | 6 | 1.0000 |
| sqlitevec-knn-tuned | 12 | 346 | 243 / 293 | 177 / 191 | 169 / 181 | 10 | 1.0000 |
| sqlitevec-scan | 12 | 904 | 894 / 919 | 585 / 697 | 229 / 767 | 6 | 1.0000 |
| sqlitevec-scan-tuned | 11 | 509 | 643 / 701 | 304 / 336 | 120 / 142 | 9 | 1.0000 |

### 100k items × 1 vector

| Engine | Load | First query | No filter | Broad | Narrow | Memory | Recall@20 |
|---|--:|--:|--:|--:|--:|--:|--:|
| memory-f32-cosine | 549 | 34 | 11.3 / 14.2 | 6.5 / 9.0 | 2.5 / 3.2 | 234 | 1.0000 |
| memory-f32-dot | 542 | 97 | 8.7 / 11.1 | 6.3 / 11.7 | 2.6 / 3.0 | 234 | 1.0000 |
| memory-f32-dot-parallel | 538 | 40 | 3.2 / 3.7 | 4.3 / 4.7 | 1.6 / 2.1 | 239 | 1.0000 |
| memory-f16-dot | 665 | 193 | 14.8 / 18.5 | 7.0 / 7.7 | 2.6 / 3.3 | 133 | 1.0000 |
| memory-f16-dot-parallel | 674 | 179 | 2.9 / 3.5 | 4.3 / 5.1 | 1.8 / 2.1 | 137 | 1.0000 |
| memory-f16-cosine-generic | 693 | 199 | 24.8 / 32.0 | 8.1 / 9.8 | 3.3 / 3.8 | 132 | 0.9952 |
| sqlitevec-knn | 11 | 143 | 147 / 223 | 124 / 141 | 120 / 128 | 7 | 1.0000 |
| sqlitevec-knn-tuned | 11 | 96 | 43.2 / 50.9 | 21.8 / 23.4 | 19.2 / 20.7 | 7 | 1.0000 |
| sqlitevec-scan | 11 | 307 | 299 / 311 | 196 / 212 | 74.9 / 79.8 | 6 | 1.0000 |
| sqlitevec-scan-tuned | 12 | 175 | 77.7 / 118 | 43.8 / 61.5 | 15.6 / 22.3 | 6 | 1.0000 |

### 10k items

At 10k items every in-memory engine answers in under 7 ms, and loads in under 0.2 s with 1 or 3 vectors per item. sqlite-vec KNN takes 15 ms (1 vector) and 46 ms (3 vectors), or 5 and 15 ms tuned. The full tables are in the [appendix](#appendix-full-desktop-report).

### Build cost

Building the `vec0` table from the BLOB table takes 4.4 s for 100k vectors and 15.5 s for 300k. That's what option A adds to every embedding-model migration (E06-S07) and every index rebuild (E01-S04-T06). B has nothing extra to build: the BLOB rows are the index.

## Findings by criterion

### Query latency

- **B is fast enough by a wide margin.** Single-threaded float32 handles 300k vectors in 24 ms without a filter. On the D031 CPU tier, expect about 40 to 50 ms, well under the 200 ms keyword and hybrid budget. Running in parallel brings it to 6 to 9 ms here.
- **Score with `Dot`, not `CosineSimilarity`.** Embeddings are normalized when they're stored, so the dot product is the cosine. It saves about 30% (24 vs 33 ms).
- **For half precision, widen to float32 and then use `Dot`.** The generic `CosineSimilarity<Half>` is twice as slow and loses accuracy: recall 0.995, against 0.9998 for widening.
- **Single-threaded half is 1.8× slower than float32**, because of the conversion. In parallel the difference disappears, since scoring is then memory-bound and half moves half the bytes.
- **Filters make B faster.** The SQL prefilter plus the bitmask cost less than the scoring they skip: the narrow filter runs in 2 to 5 ms at 300k vectors.
- **A is slow because it is brute force too, reading through SQLite.** vec0 in 0.1.x has no ANN index. It scans chunked BLOBs through the pager, so the SQLite page cache and mmap settings dominate. Tuning gives a 2 to 3× gain, but tuned KNN is still 177 to 243 ms at 300k vectors on a 20-core machine. vec0 metadata filters save only about 20%, probably because it still reads every chunk. The scalar `vec_distance_cosine()` scan is slower still, though its narrow filters do help, because SQL picks the rows first.
- **The first query after load** is 60 to 290 ms for B, which is JIT plus the first pass over the array. For A it's 100 to 900 ms, the cold page reads. Neither matters much if the index loads in the background at startup.

### Memory

- **B holds the vectors in RAM:** about 2 KB per vector in float32 and 1 KB in half precision. That's 600 / 300 MB for the 300k sizing case, and 200 / 100 MB for 100k × 1. Process growth matches that within about 40 MB, the filter and id arrays.
- **A uses almost nothing in the process:** a few MB, plus the cache size if tuned. The OS file cache holds the pages instead, and it is evicted under pressure. The cost is query latency rather than RAM.
- **Half precision is the way to fit Android.** 300 MB for 100k × 3 is still a lot on a mid-range phone. MFC-18 should measure it. If it's too much, the fallbacks are int8 quantization (150 MB, with recall to be measured), a memory-mapped file instead of a managed array, or a smaller per-item budget on the phone.

### Startup cost (index load)

- **B reads every vector at startup:** 0.55 s for 100k vectors and 1.4 to 2.0 s for 300k on this machine. On a phone, expect several times that. Mitigations, from simplest:
  1. Load in the background. Keyword search and browsing work at once, and vector search comes online when the load finishes.
  2. Store half precision, which halves the bytes to read.
  3. Later, if it's needed: a binary snapshot file next to the database, checked against a version stamp, read with one sequential read.
- **A** needs only to open the connection and load the extension (about 10 ms). The work moves to the first query.

### Combining with filters

- **A:** vec0 metadata columns support `=`, `!=`, `<`, `<=`, `>`, `>=` on scalar columns declared on the vec0 table. Each filterable field has to be copied into vec0 and kept in sync with the items table. Anything else (tags, collections, paths, joins) needs `rowid IN (subquery)` or the scalar-function scan, which is slower. Results are per vector, so per-item results need over-fetching (k × vectors per item), which the harness does.
- **B:** any SQL filter works unchanged. It returns item ids, which become a bitmask, which the scorer checks. That's the same SQL the FTS path uses (E01-S04-T05). Per-item best-vector scoring is a single pass, because an item's vectors are adjacent in the array.
- The `check` command confirms that all three paths return only matching items, and the same items as the exact baseline.

### Consistency with transactions

The requirement is spec 1.5 and 3.7: a change is searchable as soon as its transaction commits.

- **A: native.** vec0 rows live in the same database and the same transaction. Checked: the writing connection sees its own uncommitted vector; other connections don't (WAL snapshot); a rollback removes it everywhere; a committed vector and a committed delete are visible to other connections at once; the BLOB row and the vec0 row commit atomically.
- **B: by construction.** The BLOB rows are the source of truth, in the same transaction as everything else. The in-memory index is a derived cache, updated after a successful commit and never before. Checked: an uncommitted vector is never in the index; a rollback leaves nothing to undo; after the post-commit update the vector is searchable; reloading from the database gives the same index; removed items aren't returned. The constraints for E01-S04:
  - Every write path for embeddings has to go through the one component that also updates the index (a unit-of-work post-commit hook).
  - Only the app process writes the database. That's true today, since the jobs run in the same process (E05).
  - A reader inside a write transaction doesn't see its own uncommitted vectors through the index. No current flow needs that.
  - If the process crashes between the commit and the index update, nothing is lost: the index is rebuilt from the database at the next start.

### Packaging complexity

| | A: sqlite-vec | B: in memory |
|---|---|---|
| Desktop win-x64 | `sqlite-vec` NuGet, **alpha only** (0.1.7-alpha.2.1, May 2025; upstream is at 0.1.9). It needs `ExcludeAssets="contentFiles"` or it injects a source file into the project. | Nothing: `System.Numerics.Tensors` is managed code (SIMD on x64 and NEON on arm64). |
| Desktop win-arm64 | No binary, in the NuGet package or in the GitHub releases. We'd have to build it. | Nothing. |
| Android | No NuGet binaries. The GitHub releases have `.so` files for arm64, armv7a, x86_64 and i686, which would need packing per ABI. Loading through `bundle_e_sqlite3` on Android is unverified, which would be MFC-18. | Nothing. |
| Extension loading | Works on desktop: `bundle_e_sqlite3` isn't built with `SQLITE_OMIT_LOAD_EXTENSION`; `SqliteConnection.LoadExtension("vec0")` finds the DLL through deps.json; SQL `load_extension()` stays disabled unless enabled. | Not needed. |
| Maintenance risk | Pre-1.0, almost all commits from one maintainer, an alpha NuGet that lags upstream. Format changes would mean rebuilding vec0 tables. | Our own code, about 300 lines, covered by brute-force reference tests (E01-S04-T03). |

## What's deferred, and to where

- **Android numbers (MFC-18, E01-SP02):** run the harness on a mid-range phone. The harness is a console app, so on Android its engines and generator need a small host. Measure load time, the memory of `memory-f16-dot` at 100k × 3, and latency. With B chosen, MFC-18's "sqlite-vec loads on arm64" check isn't needed.
- **Production index (E01-S04-T03):** build the B index behind `ISearchIndex`, with the after-commit update rule, compaction of tombstones, and per-model separation (vectors from different models are never compared).
- **Schema (MFC-19, E01-S03):** an embeddings table keyed by item, model and frame, holding a normalized vector BLOB. No vec0 table, and no extension loading in the persistence layer.
- **Option C (ANN):** revisit only if a library goes well past 1M vectors, or if Android latency is out of budget. The harness's `IVectorSearchEngine` makes it easy to add as one more engine.

## D067 draft

> ## D067 — Vector search: embeddings in SQLite, searched in memory
> **Status:** Proposed (resolves Q5, from E01-SP01 / MFC-17)
>
> Embeddings are stored as BLOBs in SQLite and searched brute force in memory with `System.Numerics.Tensors`. The sqlite-vec extension and in-process ANN libraries are not used.
>
> - **Storage:** one row per embedding (item, model, frame index), holding an L2-normalized vector as a little-endian half-precision BLOB. Half precision matched float32 with a recall@20 of 0.9998 and halves the disk, memory and load time.
> - **Search:** all vectors of one model are held in one contiguous array, an item's vectors adjacent, and scored with `TensorPrimitives.Dot` after widening to float32 (cosine, since vectors are normalized). The scan is split across cores above about 8k vectors. An item scores as its best vector. Vectors from different models are never compared.
> - **Filters:** run as SQL on the item tables; the matching item ids become a bitmask that the scorer checks.
> - **Consistency:** SQLite is the source of truth. The in-memory index is a cache, updated only after a successful commit, by the same component that writes embeddings, and rebuilt from the database at startup (in the background).
> - **Measured (desktop, E01-SP01 harness, 100k items × 3 vectors):** 6 ms p50 in parallel and 45 ms single-threaded without filters; 300 MB of memory; about 2 s to load. sqlite-vec took 170 to 450 ms on the same data and has no stable or Android NuGet package. Android numbers come from E01-SP02.
> - **Revisit** if a library goes past about 1M vectors, or if Android memory or latency is out of budget. The next steps would be int8 quantization, a memory-mapped snapshot, or an ANN library.

## Appendix: full desktop report

Generated by the harness on 2026-10-29 (`artifacts/benchmarks/vector-search-20261029-225726.md`). The database sizes include three copies of every vector (the float32 table, the half table and vec0), so a production database is smaller. Memory columns: the managed heap after load, private bytes after the queries, and `sqlite3_memory_used()`.

#### 10,000 items x 1 vectors = 10,000 vectors

Database 81 MB (text 5 MB). Generated in 0.9 s; vec0 table built in 0.4 s.

| Engine | Load | First query | No filter p50 / p95 | Broad p50 / p95 | Narrow p50 / p95 | Managed | Private | SQLite heap | Recall |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| memory-f32-cosine | 74 | 27.7 | 1.37 / 1.67 | 0.72 / 1.05 | 0.35 / 0.46 | 20 | 42 | 2.1 | 1.0000 |
| memory-f32-dot | 75 | 30.4 | 0.54 / 0.93 | 0.58 / 0.76 | 0.25 / 0.45 | 20 | 43 | 2.1 | 1.0000 |
| memory-f32-dot-parallel | 74 | 33.1 | 0.35 / 0.59 | 0.56 / 0.67 | 0.28 / 0.49 | 20 | 45 | 2.1 | 1.0000 |
| memory-f16-dot | 68 | 188.2 | 2.31 / 3.22 | 0.72 / 0.94 | 0.30 / 0.44 | 10 | 35 | 2.1 | 1.0000 |
| memory-f16-dot-parallel | 71 | 143.3 | 0.78 / 1.02 | 0.68 / 0.79 | 0.32 / 0.44 | 10 | 37 | 2.1 | 1.0000 |
| memory-f16-cosine-generic | 66 | 211.9 | 2.47 / 2.84 | 1.00 / 1.22 | 0.42 / 0.61 | 10 | 34 | 2.1 | 0.9995 |
| sqlitevec-knn | 12 | 19.4 | 15.44 / 16.18 | 13.30 / 14.01 | 13.10 / 13.84 | 0 | 5 | 0.3 | 1.0000 |
| sqlitevec-knn-tuned | 17 | 14.9 | 4.89 / 5.31 | 2.73 / 2.99 | 2.42 / 2.75 | 0 | 6 | 0.2 | 1.0000 |
| sqlitevec-scan | 12 | 35.5 | 30.26 / 34.12 | 14.85 / 16.89 | 3.66 / 4.61 | 0 | 7 | 2.1 | 1.0000 |
| sqlitevec-scan-tuned | 12 | 21.2 | 7.44 / 9.73 | 3.46 / 3.91 | 1.28 / 1.61 | 0 | 5 | 0.2 | 1.0000 |

#### 10,000 items x 3 vectors = 30,000 vectors

Database 227 MB (text 5 MB). Generated in 3.9 s; vec0 table built in 3.6 s.

| Engine | Load | First query | No filter p50 / p95 | Broad p50 / p95 | Narrow p50 / p95 | Managed | Private | SQLite heap | Recall |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| memory-f32-cosine | 184 | 29.1 | 3.47 / 4.65 | 0.94 / 1.71 | 0.28 / 0.68 | 59 | 83 | 2.1 | 1.0000 |
| memory-f32-dot | 186 | 61.1 | 2.62 / 3.29 | 0.68 / 0.92 | 0.24 / 0.35 | 59 | 82 | 2.1 | 1.0000 |
| memory-f32-dot-parallel | 165 | 31.0 | 1.08 / 1.46 | 0.74 / 1.04 | 0.29 / 0.47 | 59 | 84 | 2.1 | 1.0000 |
| memory-f16-dot | 186 | 183.5 | 4.13 / 4.82 | 1.11 / 1.40 | 0.44 / 0.63 | 30 | 55 | 2.1 | 0.9998 |
| memory-f16-dot-parallel | 148 | 173.7 | 1.43 / 1.81 | 0.69 / 0.82 | 0.33 / 0.42 | 30 | 58 | 2.1 | 0.9998 |
| memory-f16-cosine-generic | 152 | 242.0 | 6.77 / 7.83 | 1.72 / 1.94 | 0.62 / 0.76 | 30 | 54 | 2.1 | 0.9990 |
| sqlitevec-knn | 13 | 50.4 | 45.95 / 47.19 | 40.52 / 42.98 | 39.33 / 45.58 | 0 | 6 | 0.6 | 1.0000 |
| sqlitevec-knn-tuned | 12 | 34.6 | 14.57 / 15.48 | 8.80 / 9.44 | 7.84 / 8.34 | 0 | 6 | 0.2 | 1.0000 |
| sqlitevec-scan | 12 | 97.4 | 90.91 / 103.29 | 38.78 / 44.86 | 14.82 / 16.60 | 0 | 8 | 2.1 | 1.0000 |
| sqlitevec-scan-tuned | 13 | 59.9 | 22.44 / 31.44 | 11.12 / 12.92 | 3.55 / 5.47 | 0 | 7 | 0.2 | 1.0000 |

#### 100,000 items x 1 vectors = 100,000 vectors

Database 791 MB (text 54 MB). Generated in 6.7 s; vec0 table built in 4.4 s.

| Engine | Load | First query | No filter p50 / p95 | Broad p50 / p95 | Narrow p50 / p95 | Managed | Private | SQLite heap | Recall |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| memory-f32-cosine | 549 | 34.3 | 11.26 / 14.20 | 6.47 / 8.99 | 2.47 / 3.17 | 202 | 234 | 2.1 | 1.0000 |
| memory-f32-dot | 542 | 96.9 | 8.73 / 11.09 | 6.33 / 11.70 | 2.62 / 3.01 | 202 | 234 | 2.1 | 1.0000 |
| memory-f32-dot-parallel | 538 | 40.2 | 3.19 / 3.73 | 4.27 / 4.69 | 1.55 / 2.10 | 202 | 239 | 2.1 | 1.0000 |
| memory-f16-dot | 665 | 193.2 | 14.83 / 18.52 | 7.01 / 7.65 | 2.58 / 3.33 | 104 | 133 | 2.1 | 1.0000 |
| memory-f16-dot-parallel | 674 | 178.6 | 2.87 / 3.52 | 4.27 / 5.05 | 1.80 / 2.05 | 104 | 137 | 2.1 | 1.0000 |
| memory-f16-cosine-generic | 693 | 199.1 | 24.81 / 32.03 | 8.06 / 9.77 | 3.32 / 3.80 | 104 | 132 | 2.1 | 0.9952 |
| sqlitevec-knn | 11 | 142.7 | 146.98 / 223.44 | 124.14 / 140.98 | 120.33 / 128.40 | 0 | 7 | 1.9 | 1.0000 |
| sqlitevec-knn-tuned | 11 | 96.0 | 43.21 / 50.91 | 21.78 / 23.36 | 19.20 / 20.69 | 0 | 7 | 0.2 | 1.0000 |
| sqlitevec-scan | 11 | 307.2 | 299.30 / 311.00 | 195.59 / 211.72 | 74.85 / 79.79 | 0 | 6 | 2.1 | 1.0000 |
| sqlitevec-scan-tuned | 12 | 174.9 | 77.71 / 117.70 | 43.82 / 61.46 | 15.64 / 22.25 | 0 | 6 | 0.2 | 1.0000 |

#### 100,000 items x 3 vectors = 300,000 vectors

Database 2,240 MB (text 54 MB). Generated in 15.3 s; vec0 table built in 15.5 s.

| Engine | Load | First query | No filter p50 / p95 | Broad p50 / p95 | Narrow p50 / p95 | Managed | Private | SQLite heap | Recall |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| memory-f32-cosine | 1,424 | 63.5 | 33.19 / 44.27 | 8.64 / 9.42 | 3.85 / 4.35 | 593 | 635 | 2.1 | 1.0000 |
| memory-f32-dot | 1,501 | 219.7 | 23.98 / 27.81 | 8.28 / 9.13 | 3.47 / 4.01 | 593 | 635 | 2.1 | 1.0000 |
| memory-f32-dot-parallel | 1,379 | 72.0 | 9.05 / 10.62 | 5.08 / 6.95 | 1.97 / 2.86 | 593 | 637 | 2.1 | 1.0000 |
| memory-f16-dot | 1,696 | 264.8 | 44.78 / 60.10 | 13.42 / 17.84 | 5.22 / 8.77 | 300 | 335 | 2.1 | 0.9998 |
| memory-f16-dot-parallel | 2,005 | 285.8 | 5.97 / 9.45 | 5.21 / 6.71 | 2.00 / 3.05 | 300 | 339 | 2.1 | 0.9998 |
| memory-f16-cosine-generic | 1,905 | 316.0 | 82.19 / 97.85 | 15.42 / 19.78 | 6.68 / 10.85 | 300 | 330 | 2.1 | 0.9950 |
| sqlitevec-knn | 21 | 472.0 | 446.44 / 517.69 | 376.06 / 446.87 | 376.54 / 442.78 | 0 | 6 | 2.1 | 1.0000 |
| sqlitevec-knn-tuned | 12 | 345.9 | 243.40 / 292.79 | 177.00 / 191.04 | 168.72 / 181.41 | 0 | 10 | 1.9 | 1.0000 |
| sqlitevec-scan | 12 | 903.6 | 893.71 / 918.58 | 584.94 / 697.02 | 229.26 / 767.15 | 0 | 6 | 2.1 | 1.0000 |
| sqlitevec-scan-tuned | 11 | 508.5 | 643.21 / 701.40 | 303.56 / 335.57 | 119.65 / 141.68 | 0 | 9 | 0.2 | 1.0000 |
