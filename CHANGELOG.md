# Changelog

Notable changes per release. Each release branch moves the Unreleased entries under its version (docs/development-workflow.md).

## Unreleased

### Added

- Main window layout: navigation, toolbar, media browser and details areas that features fill; side panels that resize with splitters, collapse to a rail or strip, and hide (`Ctrl+1`, `Ctrl+2`, `Ctrl+Shift+1`, `Ctrl+Shift+2`, View menu, Reset layout); the panel layout and the window placement for each monitor setup are saved across restarts (E13-S01).
- A status bar under the main window, with segments that features add without changing it, and a View › Status bar toggle saved with the layout (E13-S04).
- Keyboard shortcuts from one keymap, bound to physical keys so they work the same on English and Russian layouts, with focus scopes, text fields that keep their typing and editing keys, and a shortcut reference (`F1` or `?`, Help › Keyboard shortcuts) (E16-S01).
- Supported format registry: JPEG, PNG, WebP, GIF and MP4 are detected from their content rather than their extension; GIF and WebP files are told apart as still or animated, MP4 files need a video track, and other files are skipped with a reason (E03-S01).
- Technical metadata extraction: image EXIF, IPTC, XMP and ICC data (dimensions, orientation, date taken, camera, lens, GPS, color space) through MetadataExtractor; frame count, duration and loop count of animated GIF and WebP files; video duration, resolution, frame rate, codecs, audio track, creation time and rotation through FFprobe, with file-level metadata only when FFmpeg is missing. The date taken falls back from EXIF to the video creation time to the file modification time and records its source; damaged metadata gives partial results and warnings (library schema version 3, E03-S03).
- Job system: persisted jobs and work items (library schema version 4), a scheduler with I/O, CPU and AI pools and priorities, "visible first" boosting, cancellation, restart recovery, a clean stop when the library closes, and a confirmation when quitting while work is pending (E05-S01).
- Thumbnails in two sizes (256 and 512 pixels, WebP) for images, animated images and videos: EXIF and video rotation applied, blank first frames and black video intros skipped, videos read through FFmpeg with a placeholder and a warning when it is missing. Thumbnails are stored per item in the library's sharded cache, and a thumbnail cell shows a media type placeholder while a thumbnail is pending or after it failed (E03-S04).
- Media identity: quick fingerprints and full content hashes (XxHash128), perceptual hashes for images, a near-match search, and a file matching service with four tiers and confidence levels; a robustness test set of re-encoded, resized and converted copies (E01-S06).

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
