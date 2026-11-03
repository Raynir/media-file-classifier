# Import pipeline

How the app imports an indexed folder (E03-S07): it finds the files, fingerprints them, reads their metadata, makes their thumbnails, registers them as Pending analysis, and hashes them in full to find exact duplicates. It runs on the job system (`docs/background-processing.md`), so it never blocks the UI, survives restarts and can be cancelled. The code is in `UseCases/Import`.

## Jobs and stages

Adding folders to a library (`AddLibraryFoldersCommand`) sends `ImportFoldersCommand`, which queues one `import` job per folder. A folder whose import is still unfinished isn't queued again.

| Stage | Pool | Per | Job | What it does |
|-------|------|-----|-----|--------------|
| `import.discover` | I/O | folder | `import` | Runs `IFolderDiscovery` (`docs/file-discovery.md`). After each written batch (100 items, or the first item waited a second) it queues `import.fingerprint` for the new items, so they're processed while the scan goes on. |
| `import.fingerprint` | I/O | item | `import` | The quick fingerprint (`docs/media-identity.md`). |
| `import.media` | CPU | item | `import` | Detects the format again, extracts the technical metadata, and makes the thumbnails. Then it stores the item as **Pending** with its metadata in one transaction, publishes `MediaItemRegistered`, and queues `hash.content`. |
| `hash.content` | I/O | item | `full-hash` | The full content hash and, for images, the perceptual hash. |

- **Full hashing** runs in the folder's `full-hash` job at the low `JobPriority.FullHashing`, so it never slows an import down. `FullHashJobs` adds to the folder's unfinished hash job, or to the last one it used, which goes back to the queue if it had finished; it makes a new one when there's none or it was cancelled.
- **Parallelism** is the job system's pool limits: I/O 4, CPU the core count up to 8, overridden by `AppSettings.Processing` and `SetPoolLimit` (D151).
- **Stage keys** are stored with each work item (`ImportStages`), so they never change.
- **Item registered:** `MediaItemRegistered` is a MediatR notification with the item and folder IDs. AI analysis subscribes in M2 (E06-S06-T01). Handlers run on the job's thread-pool thread.
- **Library lifetime:** `LibrarySession` stops the job system before a library closes and starts it when one opens, which resumes interrupted work (D152).

## Progress

The import reports nothing of its own: its progress is the job's work items (D148), which the progress model (E05-S02) counts.

- **Files found:** the job's `import.fingerprint` work items. The total grows while `import.discover` is queued or running.
- **Files processed:** items with no queued or running work item left in the job.
- **Errors:** failed work items; **current stage:** the stages of running work items.
- A retried stage adds a new work item, so count the latest one per item and stage.

## Failures

A stage that throws marks the item **Error**. `MediaItem.StatusReason` holds the message, and `MediaItem.Error` (`MediaItemError`) the stage, the exception's type, and whether the failure is **permanent** (library schema version 5). The work item fails too, with the exception's message, and the other items carry on.

- **Permanent:** the file itself is the problem, e.g. `InvalidDataException`, or a file that can't be decoded at all: no thumbnail and no dimensions. Thumbnail and metadata problems short of that are warnings in `TechnicalMetadata.Warnings` (D128); a video without FFmpeg is Pending with `ThumbnailFfmpegUnavailable`.
- **Transient:** anything else, e.g. a file locked by another app.
- **Retry:** `RetryFailedItemsCommand`, for one job's folder or the whole library, queues the failed stage again. A permanent failure is retried only when the file's size or modification time has changed, and then from the fingerprint, with its old hashes cleared. There's no automatic retry yet (E05-S04).

What happens to each problem file in the sample corpus:

| File | End state |
|------|-----------|
| `corrupt.jpg` (damaged) | Error in `import.media`, permanent |
| `zero-byte.png` | Not registered: discovery skips it as unsupported (`EmptyFile`) and counts it |
| `audio-only.mp4` (no video track) | Not registered: skipped as unsupported (`NoVideoTrack`) |
| A file locked when discovery reads it | Not registered: counted as unreadable; the next import finds it again |
| A file locked later | Error in its stage, transient; a retry succeeds once it's unlocked |
| A missing folder | The job fails and the folder is marked offline |

## Resume, cancel and re-import

- **No duplicates:** discovery never registers a path the folder already has, and each stage skips work that's done: a fingerprint or content hash that's there, an item that's already registered.
- **Interrupted:** work that was running goes back to the queue when the library opens again (D152). When `import.discover` finishes, it also queues every item of the folder still Discovered without work in the job, e.g. a batch written just before the app was killed.
- **Cancelled:** finished items keep their state, and the rest stay Discovered. The next import of the folder, or the rescan (E02-S04), picks them up.

## Duplicate candidates

Items that share a full content hash are exact duplicate candidates. The indexed `hashes_content_hash` column records them, and `IMediaItemRepository.GetExactDuplicatesAsync` returns the groups. Duplicate groups with a state and review are E12.

## Benchmark

`ImportBenchmarkTests` imports 2,000 distinct 640×480 JPEGs at CPU pool sizes 1, 2, 4 and 8, with I/O 4. It only runs when `MFC_IMPORT_BENCHMARK_DIR` names a folder for the photos:

```powershell
$env:MFC_IMPORT_BENCHMARK_DIR = "D:\bench"
dotnet test --project tests/MediaFileClassifier.Infrastructure.Tests -- --filter-class "*ImportBenchmarkTests"
```

BENCHMARK_RESULTS
