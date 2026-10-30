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
- UI strings in resource files, culture-aware formatting, and a pseudo-localisation mode (`--pseudo-loc`) for layout testing (E13-S08).
- Native dependency matrix, third-party license register, and startup checks that disable video analysis when FFmpeg is missing (E01-S08).
