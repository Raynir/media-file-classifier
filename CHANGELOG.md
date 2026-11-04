# Changelog

Notable changes per release. Each release branch moves the Unreleased entries under its version (docs/development-workflow.md).

## Unreleased

### Added

- Search index engine: keyword search over the effective title, description, tags, OCR text, transcript, other descriptive fields and file path (FTS5, case and diacritics ignored, every word as a prefix, path substrings through a trigram index), in-memory vector search over half-precision embeddings of one model at a time, hybrid ranking with Reciprocal Rank Fusion, filters for media type, format, date, resolution, favorite, status, review status, collection, user tags and folder on every path, and an index rebuild. Metadata edits are searchable as soon as they are saved; libraries move to schema version 6 and rebuild their keyword index once when opened (E01-S04).

### Fixed

- A finished job no longer stays in the progress summary and the status bar when its last work items finish at the same time (MFC-985).

## M1 — 2026-11-04

### Added

- Main window layout: navigation, toolbar, media browser and details areas that features fill; side panels that resize with splitters, collapse to a rail or strip, and hide (`Ctrl+1`, `Ctrl+2`, `Ctrl+Shift+1`, `Ctrl+Shift+2`, View menu, Reset layout); the panel layout and the window placement for each monitor setup are saved across restarts (E13-S01).
- Settings window (Tools → Settings…, Ctrl+,): settings registered by features, with generated controls per category, search by name, reset per category or for all, and app or library scope shown on each setting; the theme choice is now saved, and `settings.json` moves to schema version 2 (E17-S01).
- A status bar under the main window, with segments that features add without changing it, and a View › Status bar toggle saved with the layout (E13-S04).
- Welcome screen and create-library dialog: a first launch offers to create a library in `%USERPROFILE%\Media File Classifier` or another checked location, opens it, and asks for the folders to index; Library → New library… does the same later (E02-S01).
- Opening libraries: Library → Open library…, a recent-libraries list on the welcome screen that marks unreachable libraries and lets you remove them, the last library reopening on startup (General → Open the last library on startup), clear messages for libraries in use, missing or from a newer app, and a progress window while an older library is updated, with an offer to restore its backup if the update fails (E02-S02).
- Keyboard shortcuts from one keymap, bound to physical keys so they work the same on English and Russian layouts, with focus scopes, text fields that keep their typing and editing keys, and a shortcut reference (`F1` or `?`, Help › Keyboard shortcuts) (E16-S01).
- Supported format registry: JPEG, PNG, WebP, GIF and MP4 are detected from their content rather than their extension; GIF and WebP files are told apart as still or animated, MP4 files need a video track, and other files are skipped with a reason (E03-S01).
- Technical metadata extraction: image EXIF, IPTC, XMP and ICC data (dimensions, orientation, date taken, camera, lens, GPS, color space) through MetadataExtractor; frame count, duration and loop count of animated GIF and WebP files; video duration, resolution, frame rate, codecs, audio track, creation time and rotation through FFprobe, with file-level metadata only when FFmpeg is missing. The date taken falls back from EXIF to the video creation time to the file modification time and records its source; damaged metadata gives partial results and warnings (library schema version 3, E03-S03).
- Job system: persisted jobs and work items (library schema version 4), a scheduler with I/O, CPU and AI pools and priorities, "visible first" boosting, cancellation, restart recovery, a clean stop when the library closes, and a confirmation when quitting while work is pending (E05-S01).
- Thumbnails in two sizes (256 and 512 pixels, WebP) for images, animated images and videos: EXIF and video rotation applied, blank first frames and black video intros skipped, videos read through FFmpeg with a placeholder and a warning when it is missing. Thumbnails are stored per item in the library's sharded cache, and a thumbnail cell shows a media type placeholder while a thumbnail is pending or after it failed (E03-S04).
- Media browser: an open library shows its items newest first in a virtualised thumbnail grid that stays smooth at 100k items, loads results in pages and thumbnails in the background (on-screen items get theirs first), shows Pending and Error badges, video durations and an animated marker, and opens an item in its default app on double-click or Enter. The thumbnail size (96–384 px) changes with the slider, Ctrl+mouse wheel or `=` / `-`, and is remembered per library (E14-S01).
- Import pipeline: adding folders imports them in the background. Files are found, fingerprinted, read for metadata and thumbnailed in parallel, appear as Pending analysis as they're registered, and are hashed in full afterwards at low priority so exact duplicates are grouped; a failed file is marked Error with its stage and can be retried, damaged files only once they change; imports resume after a restart, can be cancelled, and never create duplicate items (library schema version 5, E03-S07).
- Navigation panel: Library (All media, Recently imported) and Folders sections with live counts; the folders tree shows subfolders from the indexed items, marks Offline folders, and scopes the media browser to the selected folder or subfolder; Excluded items are no longer shown; sections collapse and stay collapsed; the collapsed panel is an icon rail; folders dropped on the panel are added through the folders dialog (E13-S02).
- Indexed folders: Library → Manage folders… lists the folders with their state and item counts; adding a folder imports it, a folder inside an indexed one is refused and indexed folders inside a new one are merged into it with their items and metadata; excluded subfolders and the hidden-file and cloud-file toggles mark excluded items Excluded and bring them back when the exclusion is removed; removing a folder removes its items from the library only, after a confirmation; unreachable folders show as Offline with their items kept (E02-S03).
- Media identity: quick fingerprints and full content hashes (XxHash128), perceptual hashes for images, a near-match search, and a file matching service with four tiers and confidence levels; a robustness test set of re-encoded, resized and converted copies (E01-S06).
- Progress reporting: counts per job and stage taken from the job system's work items, totals marked "so far" while a job is still finding work, an estimated time left from the last 30 seconds of throughput, and a status bar indicator ("Fingerprinting: 120 of 340 · 12 min left") updated at most twice a second (E05-S02).
- Selection in the media browser, as in Windows Explorer: click, Ctrl+click and Shift+click; arrows, Page Up/Down, Home and End move the focus and select, with Shift extending and Ctrl moving only the focus (Ctrl+Space toggles); Select all (`Ctrl+A`, Edit menu) selects every result without loading them and leaves out items added later; Esc or a click between tiles deselects. The selection keeps items still in the results after a refresh, and the status bar shows how many are selected (E14-S03).
- Context menu in the media browser: right-click or the Menu key on the selection offers Open in default app (`Ctrl+O`), Reveal in File Explorer (`Ctrl+Shift+R`) and Copy file path (`Ctrl+Shift+C`, every selected path, one per line); right-clicking an unselected tile selects it, entries that don't apply are hidden, and Open and Reveal are disabled with the reason for missing files and offline folders (E14-S04).
- Preview and details panel: the selected item's preview, title, quick actions (Open, Reveal) and Overview (description, tags) and Technical (type, format, dimensions, size, status, date taken, camera, video details, location, folder, path) tabs; the preview loads a screen-sized version of the original first and full resolution when zoomed (mouse wheel, Fit, 1:1, drag to pan) with EXIF orientation applied, and shows the thumbnail for missing files and offline folders; it can be resized and collapsed, and its tab and size are remembered. Enter or double-click in the grid shows the panel, ←/→ there move through the results, Enter opens the default app and Esc goes back to the grid (E14-S05).

## M0 — 2026-11-01

### Added

- Layered solution structure, shared build configuration and code style rules (E01-S01).
- Empty main window, resolved through dependency injection and bound to a view model.
- Rolling log files and logging of unhandled exceptions.
- App settings stored as JSON, including the main window placement.
- Test helpers for temporary library databases, and a sample media corpus of edge-case files (E01-S11).
- CI builds and tests pull requests and the long-lived branches on GitHub Actions.
- Platform services, file access through folder handles, and capability flags with reasons for the UI (E01-S07).
- Core domain model: libraries, folders, media items, three metadata layers with provenance, analysis runs, embeddings and collections (E01-S02).
- UI strings in resource files, culture-aware formatting, and a pseudo-localisation mode (`--pseudo-loc`) for layout testing (E13-S08).
- Light and dark themes that follow Windows, with an override (`--theme=light|dark`), provisional design tokens and icons, and a placeholder main window layout with collapsible side panels (E13-S06).
- A command registry and a main menu (Library, View, Tools, Help so far), with Help → Open log folder, theme switching and a pseudo-localisation toggle (E13-S05).
- Native dependency matrix, third-party license register, and startup checks that disable video analysis when FFmpeg is missing (E01-S08).
- Library package format specification: contents, SQLite container, versioning, and draft import and merge rules (E01-S09).
- Library storage: `<name>.mfc` folders with a SQLite database, a thumbnail cache and a lock file; schema migrations with a backup on open; repositories; integrity check and online backup (E01-S03).
- Android app head with the shared shell and an Android smoke-test page for SQLite, ONNX Runtime, folder access and vector search; CI builds it on pull requests (E01-SP02).
- Media items record when their favorite mark and review status last changed, so library package merges can keep the newer change (library schema version 2, E01-S02).
- Evaluation corpus: 351 openly licensed media files fetched from a GitHub release, 105 English and 110 Russian queries with relevance labels, OCR ground truth for 80 images, and a runner that reports recall@10, MRR and character error rate against the previous run and per-language baselines; private runs on personal media stay out of Git (E01-S10).
- Embedding model spike: ten candidate image-text models compared on the evaluation corpus, SigLIP 2 base-patch16-256 chosen, and the first recall@10 and MRR baselines recorded (E06-SP01).

### Changed

- Capability names and reasons come from the resource files, so disabled-command tooltips are localised and pseudo-localised too (E13-S08).
