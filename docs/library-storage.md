# Library storage

How a library is stored on disk (E01-S03). The code is in `src/MediaFileClassifier.Infrastructure.DataAccess`; the interfaces are in `Infrastructure.Abstractions/Libraries` and `Infrastructure.Abstractions/Repositories`.

## Folder layout

A library is one self-contained folder, so it can be backed up or moved as a unit (D003, D004).

```
<name>.mfc/
  library.db         SQLite database (D003)
  library.db-wal     write-ahead log, while the library is open
  library.db-shm     WAL index, while the library is open
  library.lock       held by the app instance that has the library open
  thumbnails/
    a7/              last two hex digits of the item ID
      019a3b2c-...-0000000000a7.webp
  backups/
    library-v1-20261030-120000-000.db   taken before a schema migration
```

- **Create:** `ILibraryStore.CreateAsync(parent, name)` creates the folder, the database at the latest schema, the library record and `thumbnails/`. It fails with `AlreadyExists` if a non-empty folder of that name exists. If creating fails or is cancelled after the lock is taken, it removes what it made, so the library can be created again.
- **Open:** `ILibraryStore.OpenAsync(folder)` takes the lock, migrates an older schema after a backup, and returns an `IOpenLibrary`. Disposing it waits for queued writes, closes every connection, which folds the WAL back into `library.db`, and releases the lock. Reads and writes on a closed library throw `ObjectDisposedException`.
- **Threads:** Microsoft.Data.Sqlite runs its async methods synchronously, so reads, backups, integrity checks, create and open run on a thread-pool thread, and writes on the writer's. Calling them from the UI thread doesn't block it.
- **Errors:** `LibraryOpenException.Error` is a code (`NotFound`, `AlreadyExists`, `InUse`, `NewerVersion`, `NotALibrary`, `NotWritable`) for the UI to map to a resource string (E13-S08).

### Thumbnails (D078)

Thumbnails are files on disk, not database rows, to keep the database small; at about 20 KB each they're about 2 GB per 100k items.

- **Sharding:** there's one level of 256 folders, named by the last two hex digits of the item ID (`LibraryLayout.GetThumbnailPath`). That's about 400 files per folder at 100k items.
  - The tail of a UUIDv7 is random, so the files spread evenly.
  - Its head is a timestamp and would cluster.
- **Keyed by item:** deleting or regenerating an item's thumbnail touches only its own files. Duplicates get separate thumbnails.
- **Two sizes:** each item has `<item-id>-256.webp` and `<item-id>-512.webp`, WebP at quality 80, 256 and 512 pixels on the longest edge, never upscaled (E03-S04). `IThumbnailCache` finds, writes and deletes them; see `docs/thumbnails.md`.

### Lock file (D077)

`library.lock` is opened with no sharing (`FileShare.None`) and held for as long as the library is open.

- **No stale locks:** the operating system releases the lock when the process ends, even after a crash.
- **Diagnostics:** the file holds the process ID, machine name and time.
- **Other platforms:** on Android and Linux, .NET maps this to an advisory `flock`, which other .NET processes respect.
- **Errors:** a lock held by another instance gives `InUse`. A lock file that can't be written, e.g. in a read-only folder, gives `NotWritable`.
- **Limitation:** locking over SMB or NFS shares isn't reliable.

## Connections and pragmas

`LibraryConnectionFactory` opens every connection with these settings:

| Pragma | Value | Why |
|--------|-------|-----|
| `journal_mode` | `WAL` | Readers and the writer don't block each other. |
| `synchronous` | `NORMAL` | Safe with WAL; a power cut can lose the last commits but doesn't corrupt the database. |
| `foreign_keys` | `ON` | SQLite leaves them off by default. |
| `busy_timeout` | 5000 ms | A backstop; the app's own writes never collide (see below). |
| `wal_autocheckpoint` | `0`, on the write connection only | Commits don't checkpoint; a background checkpointer does (see below). |

**FTS5** is compiled into the SQLite build that `SQLitePCLRaw.bundle_e_sqlite3` ships (`sqlite_compileoption_used('ENABLE_FTS5')` returns 1). No extension is loaded (D067). Tests cover both the pragmas and FTS5.

## Write concurrency (D072)

SQLite allows one writer at a time, so all writes go through one `LibraryWriter` per open library.

- **One at a time:** a channel queues the writes, and they run in order on a single connection, each in its own transaction. The app never gets `SQLITE_BUSY` from its own writes.
- **Fair:** a user edit waits only for the write ahead of it, never for a whole import.
- **Reads:** they don't go through the writer. Each read opens its own pooled connection and runs in parallel with the writer, thanks to WAL.
- **Bulk inserts:** `IMediaItemRepository.AddRangeAsync` writes 100 items per transaction, so other writes get their turn between batches.
- **Measured** by the stress test in `WriteConcurrencyTests`: two jobs inserting 10,000 items each, three readers paging, and a user edit every 10 ms. There were no busy errors.

  | | Batches of 500 | Batches of 100 |
  |---|---|---|
  | Edit wait, p50 | 170 ms | 20 ms |
  | Edit wait, max | 240 ms | 45 ms |
  | Read, p99 | | under 30 ms |

  Throughput was the same for both batch sizes.
- **Checkpoints:** SQLite's automatic checkpoint runs inside the commit that fills the WAL past 1000 pages. It copies the WAL into the database file and syncs it, and the write and every write queued behind it wait. On the CI runner (2 CPUs) that took 0.2–2 s, so an edit took up to 3.5 s (MFC-982). The writer turns the automatic checkpoint off. After each write, it asks `WalCheckpointer` for a checkpoint. That runs `PRAGMA wal_checkpoint(PASSIVE)` on its own connection, at most once every 500 ms, and never blocks readers or the writer. Closing the library still checkpoints the rest. Measured on the CI runner with the stress test, five runs before and three after:

  | | Checkpoint in the commit | Checkpoint in the background |
  |---|---|---|
  | Batch, p99 | 195–1320 ms | 66–180 ms |
  | Edit wait, max | 237–2082 ms | 110–315 ms |

  Throughput was about the same.

- **Rule for callers:** code running inside a write (e.g. the change passed to `IItemMetadataRepository.UpdateAsync`) must not wait for another library write. The writer rejects such a write with `InvalidOperationException` instead of deadlocking.
- **Cancellation:** a queued write is dropped as soon as its token is cancelled. A started write is rolled back if it stops on the token.

## Schema (D071)

The schema is built with EF Core from the domain entities (`docs/domain-model.md`), which are mapped directly.

- **Names:** tables and columns are snake_case: `media_item`, `accepted_from_run_id`.
- **IDs:** canonical 36-character lowercase text. UUIDv7 text sorts in creation order to the millisecond (D066).
- **Times:**
  - A `DateTimeOffset` is stored as INTEGER UTC Unix milliseconds, so date filters and sorts (spec 1.7) run in SQL. Loaded values are UTC.
  - The camera's local "date taken" is a `DateTime`, stored as milliseconds of the same wall-clock time; its offset is in `date_taken_offset`.
  - A `TimeSpan` is stored as INTEGER milliseconds.
- **Hashes:**
  - 128-bit hashes are 32 lowercase hex digits.
  - The 64-bit perceptual hash is an INTEGER with the same bits.
  - `hashes_content_hash` and `hashes_fingerprint` are indexed.
- **Field values:** a JSON string for a text value, a JSON array for a list value.
- **JSON columns:** `FolderRules`, `LibrarySettings` and a job's item IDs are JSON. Serialization is source-generated, so it trims safely on Android.
- **Tags:**
  - `tag` holds the display text. `tag_key` holds the tag in upper case (`ToUpperInvariant`), which EF fills when a row is added.
  - Tags are matched, grouped and keyed by `tag_key`. That matches `TagName`'s `OrdinalIgnoreCase` comparison in every alphabet, where SQLite's `NOCASE` folds only A–Z.
- **Embeddings:** one row per vector, holding a little-endian half-precision BLOB of 2 bytes per dimension (D067).
- **Composite values:** `RunReference`, `FileHashes` and `GeoLocation` are EF complex types, so each part is its own column. EF can't pass a complex type to a constructor, so `MediaItem`, `UserFieldValue`, `TagAssignment` and `TagRejection` have a private parameterless constructor for EF. Those four skip their constructor checks when loaded; the data was checked when it was written.
- **Tag assignments:** an assignment has no ID of its own, so `tag_assignment` has an integer row key.
- **Cascades:**
  - Deleting an item deletes everything it owns: values, runs, tags, keyframes, embeddings and collection memberships.
  - Deleting a run deletes its values and tags.
  - Models and folders can't be deleted while anything refers to them.
- **Metadata updates:** `IItemMetadataRepository.UpdateAsync` loads the item's rows into an `ItemMetadata`, applies the domain change, and replaces the item's rows, all in one write.

### Schema versions and migrations

- **Version table:** each EF migration is one schema version. `schema_version` (EF's migrations history table) lists the applied ones, and `library.schema_version` holds their count. A test checks that the count equals `Library.CurrentSchemaVersion`.
- **On open:**
  - If migrations are pending, the database is copied with `VACUUM INTO` to `backups/library-v<old>-<time>.db`, then migrated. The time has milliseconds, `yyyyMMdd-HHmmss-fff`.
  - `library.schema_version` is set to the migration count on every open, so a version left stale by a crash between migrating and updating it is repaired.
  - A library with migrations the app doesn't know fails with `NewerVersion`.
  - A database with no migrations fails with `NotALibrary` and is left untouched.
- **Adding a migration:**
  1. Change the domain model or its mapping in `Database/Configurations`.
  2. Run:
     ```powershell
     dotnet tool restore
     dotnet ef migrations add <Name> -p src/MediaFileClassifier.Infrastructure.DataAccess -s src/MediaFileClassifier.Infrastructure.DataAccess -o Migrations
     ```
  3. Raise `Library.CurrentSchemaVersion` by one.
  4. Run the tests. `LibraryStoreTests.OpenAsync_LibraryAtEachOlderVersion_BacksUpAndUpgradesKeepingData` rolls a library back to every older version and upgrades it again. It also checks that the upgraded columns match a new library's, and that an item keeps its values and can save new ones.
- **Generated code:** files in `Migrations/` are marked `generated_code` in `.editorconfig`, so the style analyzers skip them. EF can't index complex-type columns, so the hash indexes are hand-written SQL in the initial migration.

## Integrity check and backup (D073)

`IOpenLibrary.Maintenance` provides three operations:

- **`CheckIntegrityAsync`:** runs `PRAGMA integrity_check` on its own connection; writes can continue.
- **`BackupAsync(path)`:** runs `VACUUM INTO` on its own connection.
  - It reads one consistent snapshot, so writes can continue.
  - The copy is compacted and self-contained, in rollback-journal mode with no `-wal` file. Put it in a `<name>.mfc` folder and it opens as a library.
  - A test takes a backup during a 10,000-item import and opens it.
- **`OptimizeAsync(vacuum)`:** runs `ANALYZE`, and optionally `VACUUM` first. It's queued on the writer.

## Android notes (D004)

- **SQLite build:** the same `e_sqlite3` build ships on Android. E01-SP02 checks WAL and FTS5 there.
- **EF Core startup:** a compiled model (`dotnet ef dbcontext optimize`) would cut startup time and keep trimming safe. It isn't used yet; add it with the Android head.
