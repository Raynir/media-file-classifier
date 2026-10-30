# Vector search benchmark harness

Built for the MFC-17 spike (findings: [docs/spikes/vector-search.md](../../docs/spikes/vector-search.md)). MFC-19 and E01-S04 reuse it to check the production search index against the same libraries.

## Run it

From the repository root:

```powershell
dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks
```

That's the full desktop matrix: 10k and 100k items, with 1 and 3 vectors per item, through every engine. It takes about 10 to 15 minutes. The first run also generates the libraries, about 3.2 GB in `%TEMP%\mfc-vector-bench`. Later runs reuse them. The report is printed and saved as Markdown in `artifacts\benchmarks\`.

Other commands:

```powershell
# Smoke run: 2,000 items, 20 queries, under a minute.
dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks -- --quick

# Yes/no checks: extension loading, filters, transaction visibility.
dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks -- check

# A subset.
dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks -- --items 100000 --vectors-per-item 3 --engines memory-f16-dot,sqlitevec-knn

# All options.
dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks -- --help
```

Always use `-c Release`. Close heavy apps first, because the numbers are wall-clock times.

## What it measures

For every library and engine, a worker process of its own measures:

- **Load:** the time from opening the database to being ready to query. The in-memory engines read every vector here. sqlite-vec only opens the connection and loads the extension.
- **First query:** the first search after load. It includes JIT and cold pages, so it's what the user waits for on the first search after startup.
- **Latency:** p50 and p95 over up to `--queries` queries per filter scenario, after a warm-up. A scenario stops early when it runs past `--budget-seconds`.
- **Memory:** managed heap growth after load, and private bytes growth after the queries (this includes native memory such as SQLite's page cache). SQLite heap is `sqlite3_memory_used()`.
- **Recall:** the share of the exact float32 top-k (`memory-f32-cosine`) that the engine also returned.

Results are per item: an item with several vectors (keyframes, or two embedding models during a migration) scores as its best vector.

## The synthetic libraries

`Data/LibraryGenerator.cs` builds one SQLite file per `LibrarySpec`. It's deterministic for a given seed.

- `media_items`: a Windows path, a 150 to 600 character description, 3 to 12 tags, OCR text on 15% of items, a date taken, and the filter columns `library_id` (60/30/10%) and `kind` (70% photos, 20% videos, 10% animated).
- `embeddings`: L2-normalized float32 BLOBs, `--vectors-per-item` per item, clustered around 256 concepts, so ranking and recall mean something.
- `embeddings_f16`: the same vectors in half precision.
- `vec_embeddings`: a sqlite-vec `vec0` table, with the filter columns as metadata columns.
- `queries`: 500 query vectors, so every engine runs the same queries.

Filter scenarios: **None**; **Broad**, library 1 photos since 2020 (about 16% of items); **Narrow**, library 2 videos (about 6%).

## Engines

| Engine | What it is |
|---|---|
| `memory-f32-cosine` | Prototype B: float32 in one array, `TensorPrimitives.CosineSimilarity`. The exact reference. |
| `memory-f32-dot` | The same, scored with `TensorPrimitives.Dot`. Equal to cosine for normalized vectors. |
| `memory-f32-dot-parallel` | The same, split across all cores. |
| `memory-f16-dot` | Half precision storage, widened to float32 per vector, then `Dot`. |
| `memory-f16-dot-parallel` | The same, split across all cores. |
| `memory-f16-cosine-generic` | Half precision with the generic `TensorPrimitives.CosineSimilarity<Half>`. |
| `sqlitevec-knn` | Prototype A: KNN on the `vec0` table, filters as vec0 metadata constraints. |
| `sqlitevec-scan` | Prototype A: `vec_distance_cosine()` over the BLOB table, filters as ordinary SQL. |
| `sqlitevec-*-tuned` | The same with `mmap_size` 4 GB and a 1 GB page cache, instead of SQLite's 2 MB default. |

To try another approach (for example option C, an ANN library), implement `IVectorSearchEngine` and add it to `EngineCatalog`.

## Packaging note

The `sqlite-vec` NuGet package ships `vec0` for win-x64, linux-x64/arm64 and osx only. Android and win-arm64 binaries have to come from the sqlite-vec GitHub releases. See the findings doc.
