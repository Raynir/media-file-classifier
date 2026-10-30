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
