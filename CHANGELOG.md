# Changelog

Notable changes per release. Each release branch moves the Unreleased entries under its version (docs/development-workflow.md).

## Unreleased

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

### Changed

- Capability names and reasons come from the resource files, so disabled-command tooltips are localised and pseudo-localised too (E13-S08).
