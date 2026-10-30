# Library package format

The format for exporting a library and importing it on another device (E01-S09). It serves:

- metadata backup and transfer (spec 3.13),
- taking a desktop library to the Android app (D004, E20-S03),
- sending phone changes back to the desktop (E20-S08).

This is a specification only. The writer is E07-S08, the reader and merge are E07-S09, and the phone side is E20. The merge rules below are a draft and are finalised in E07-S09.

Decisions: D074 (container and contents), D075 (versioning), D076 (merge rules).

## Contents (E01-S09-T01)

A package holds what a target device needs to search the media without analysing it again (spec 3.13), and nothing that is tied to the source machine.

### Included

| Data | Domain source | Notes |
|------|---------------|-------|
| Library name and active search model | `Library.Name`, `LibrarySettings.ActiveSearchModelId` | The name is the default for a new library on import. |
| Indexed folder definitions | `IndexedFolder.Id`, `Rules` | Plus a **label**: the last segment of `DisplayPath` (e.g. `Photos`). |
| Items | `MediaItem` | Relative path, media type, size, file dates, import date, review status, favorite flag. |
| Fingerprints, content hashes, perceptual hashes | `MediaItem.Hashes` | Needed to match files on the target (E01-S06). |
| Technical metadata | `TechnicalMetadata`, `TechnicalFieldValue` | Including the raw JSON, scrubbed of absolute paths (see below). |
| Keyframes | `Keyframe` | For per-frame embeddings and video seeking. |
| User values with provenance | `UserFieldValue` | Origin, `RunReference` snapshot, update date, suggestion run. |
| AI values with provenance | `AnalysisRun`, `AiFieldValue` | **Active runs only**: each analyzer's active completed run, with its rejected-in-review date. |
| Tags | `TagAssignment` | Technical and user tags, and the AI tags of the included runs. |
| Rejected tags | `TagRejection` | Tombstones, so removed tags don't come back on the target. |
| Collections and memberships | `Collection`, `CollectionMembership` | |
| Saved searches | `SavedSearch` | Folder filters refer to folder IDs in the package. |
| Model registrations | `ModelRegistration` | Every model an included run or embedding refers to. |
| Embeddings | `Embedding` | **By default only the active search model's.** Export options: all models, or none (E07-S08-T02). |

### Excluded

| Data | Why |
|------|-----|
| Thumbnails | Regenerated on the target device. |
| Absolute machine paths: `IndexedFolder.DisplayPath`, `PlatformHandle` | Meaningless on another device, and private. |
| Absolute paths inside `TechnicalMetadata.RawMetadataJson` | Extractors can record the source file path (e.g. a `SourceFile` or `Directory` key). The writer removes them; E07-S08 fixes the key list once the extractor (E03-S03) is chosen. |
| App settings, and `LibrarySettings` other than the active search model | Device preferences, e.g. whether to analyse automatically. |
| API keys and other secrets | Never exported. |
| Previous (inactive) analysis runs | Only used to compare runs in review (E08-S06). About 15–20% of a library's size (`docs/domain-model.md`). |
| Failed, running and pending runs, and run errors | Device-local processing state. |
| Processing jobs | Device-local processing state. |
| Item processing status and status reason | The target works out its own status from its own files. The source status is kept in the package for information only. |
| Items with status `Excluded` | The user removed them from the library (E02-S10). |
| Embeddings of other models, unless the user ticks "all models" | Size: about 150 MB per model at 100k items (512 dimensions, half precision, ~1.5 vectors per item). |

### Scope

The export scope is the whole library, selected folders, or the current selection (E07-S08). A partial package contains the scoped items and only the folders, collections, runs, models, keyframes and embeddings they refer to. Collections keep only the memberships of included items.

## Container (E01-S09-T02)

**The package is a single SQLite database file (D074).** Every platform already uses SQLite (D003), so no new parser is needed, and:

- the writer and reader stream row by row, which keeps memory low on the phone (E20-S03);
- embeddings stay binary BLOBs in the D067 layout, with no base64;
- the import attaches the package to the library (`ATTACH`) and validates and merges with SQL;
- old packages are upgraded with ordinary migrations (see Versioning).

It isn't human-readable. The optional flat JSON/CSV export (E07-S08-T03) covers that, and isn't a package.

### File

- **Extension:** `.mfcpkg` (working name; E07-S08 may change it before release).
- **`PRAGMA application_id`** = `0x4D464350` (`MFCP`), so the reader recognises a package before reading any table.
- The writer creates the file in a temporary location, fills it in one transaction, runs `VACUUM`, and moves it to the destination only when it's complete. A partial file is never left at the destination.
- `journal_mode` is `DELETE`, so the package is one file with no `-wal` or `-shm` companions.

### Encoding

The package defines its own encodings, so it stays a stable contract even when the library schema (E01-S03) changes.

| Kind | Encoding |
|------|----------|
| IDs (`Id<T>`, UUIDv7, D066) | 16-byte BLOB, RFC 9562 byte order (`Guid.ToByteArray(bigEndian: true)`). |
| Timestamps (`DateTimeOffset`) | TEXT, ISO 8601 round-trip format with offset (`"o"`). |
| `DateTaken` | TEXT, ISO 8601 without offset, plus the offset in its own column (as in the domain). |
| Durations and keyframe timestamps | INTEGER, milliseconds. |
| Fingerprint and content hash (`UInt128`) | 16-byte BLOB, big-endian. |
| Perceptual hash (`ulong`) | 8-byte BLOB, big-endian. |
| Enums | TEXT, the member name (e.g. `AnimatedImage`), never the number. |
| Field values (`FieldValue`) | TEXT for text fields; a JSON array of strings for list fields. |
| `RunReference` snapshots | JSON object: run ID, analyzer, model, model version, prompt version, date. |
| Folder rules, filters | JSON, as in the domain. |
| Embedding vectors | BLOB, L2-normalised, little-endian half precision (D067). |
| Relative paths | TEXT, `/`-separated, as `RelativePath` in the domain. |

### Tables

Table and column names below are the draft; E07-S08-T01 fixes them. Foreign keys are declared and checked on import.

| Table | Key | Columns |
|-------|-----|---------|
| `manifest` | `key` | `value` (see below) |
| `library` | — (one row) | `id`, `name`, `active_search_model_id` |
| `folders` | `id` | `label`, `include_subfolders`, `include_patterns`, `exclude_patterns` |
| `items` | `id` | `folder_id`, `path`, `media_type`, `size_bytes`, `file_created_at`, `file_modified_at`, `imported_at`, `fingerprint`, `content_hash`, `perceptual_hash`, `review_status`, `review_status_changed_at`¹, `is_favorite`, `favorite_changed_at`¹, `source_status` |
| `technical_metadata` | `item_id` | the `TechnicalMetadata` properties, `raw_metadata_json`, `extracted_at` |
| `technical_field_values` | `item_id`, `field` | `value`, `read_at` |
| `keyframes` | `id` | `item_id`, `index`, `timestamp_ms`, `frame_number` |
| `model_registrations` | `id` | `provider_id`, `model_name`, `version`, `file_hash`, `embedding_dimension`, `registered_at` |
| `analysis_runs` | `id` | `item_id`, `analyzer`, `provider_id`, `model_id`, `model_version`, `prompt_version`, `started_at`, `completed_at`, `rejected_at` |
| `ai_field_values` | `run_id`, `field` | `value`, `model_id`, `confidence`, `created_at` |
| `user_field_values` | `item_id`, `field` | `value`, `origin`, `accepted_from`, `suggestion_run_id`, `updated_at` |
| `tag_assignments` | `item_id`, `tag`, `source`, `run_id` | `model_id`, `confidence`, `origin`, `accepted_from`, `created_at` |
| `tag_rejections` | `item_id`, `tag` | `rejected_from`, `rejected_at` |
| `collections` | `id` | `name`, `parent_id`, `created_at` |
| `collection_memberships` | `collection_id`, `item_id` | `added_at` |
| `saved_searches` | `id` | `name`, `query_text`, `filters_json`, `sort`, `created_at` |
| `embeddings` | `id` | `item_id`, `model_id`, `kind`, `keyframe_id`, `dimension`, `vector`, `created_at` |

¹ Not in the domain model yet: gap G1.

`suggestion_run_id` is kept only when that run is in the package; otherwise it's empty.

### Manifest

| Key | Example | Meaning |
|-----|---------|---------|
| `format_version` | `1.0` | Package format, `MAJOR.MINOR` (see Versioning). |
| `package_id` | UUIDv7 | Identifies this package, for the import report and repeat imports. |
| `created_at` | `2027-03-01T10:15:00.0000000+00:00` | Export date. |
| `app_name` | `MediaFileClassifier` | Writer. |
| `app_version` | `1.4.0` | Writer's app version. |
| `app_platform` | `windows` or `android` | Writer's platform. |
| `source_schema_version` | `7` | The source library's schema version. For information only. |
| `source_library_id` | UUIDv7 | `Library.Id` of the source. |
| `scope` | `library`, `folders` or `selection` | What was exported. |
| `embedding_models` | JSON array of model IDs | Models whose embeddings are included; empty if none. |
| `item_count` | `48213` | For progress and validation. |

Readers ignore manifest keys they don't know.

### Size

Roughly, for 100k items: the metadata is 0.5–1 GB (the active AI layer alone is about 0.45 GB, `docs/domain-model.md`), plus about 150 MB per included embedding model.

## Versioning and compatibility (E01-S09-T03)

**The package has its own format version, `MAJOR.MINOR`, independent of the library schema version (D075).**

### Format version and schema version

- The library schema version (`Library.SchemaVersion`, E01-S03) changes with every migration, including internal tables such as jobs and caches. The package format changes only when the **package's** tables or encodings change.
- A schema migration that touches exported data must update the writer's mapping. A round-trip test (export, then import into an empty library, then compare) catches a mapping that wasn't updated.
- `source_schema_version` in the manifest is for diagnostics only. The reader never decides anything from it.
- The first implemented format (E07-S08) is `1.0`.

### What bumps which number

| Change | Bump |
|--------|------|
| A new table, or a new nullable column | MINOR |
| A new manifest key | MINOR |
| A new enum value (e.g. a new `DescriptiveField`) | MINOR |
| A removed or renamed table or column, or a column that becomes required | MAJOR |
| A changed encoding (e.g. vector layout, ID byte order) | MAJOR |
| A changed meaning of an existing column | MAJOR |

### Reading rules

| Package | What happens |
|---------|--------------|
| Not a SQLite file, wrong `application_id`, or no manifest | Refused: "This file isn't a Media File Classifier library package." |
| Same MAJOR, any MINOR | Imported. Tables and columns the reader doesn't know are ignored. Rows with an enum value it doesn't know are skipped. The import report says how much was skipped and suggests updating the app. |
| Older MAJOR, still supported | Copied to a temporary file and upgraded one major version at a time (1→2→3), then imported. The original file isn't changed. |
| Older MAJOR, no longer supported | Refused: "This package was made by Media File Classifier {app_version} (format {format_version}), which this version can no longer read." |
| Newer MAJOR | Refused: "This package was made by Media File Classifier {app_version} (format {format_version}). This app reads format {major}.x. Update the app to import it." |
| Fails validation (`PRAGMA integrity_check`, foreign keys, item count) | Refused, with the reason, before anything is merged. |

- Each MAJOR bump ships an upgrade step from the previous MAJOR. Steps are kept as long as that format is supported; dropping support for an old format needs a decision.
- The desktop app and the Android app (D058 version lines) both write the newest format they know. Because MINOR changes stay readable, a phone that is a minor version behind can still import a desktop package.
- A MAJOR bump is a breaking change in the sense of D058 (2.0.0).
