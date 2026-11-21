# Manual rescan

A rescan (E02-S04) compares the files on disk with the library and brings the library up to date: new files are imported, moved or renamed files keep their items, changed files are refreshed, and files that are gone are marked Missing. It never deletes anything by itself, and it never touches files on disk.

## Starting a rescan

- **Rescan library** (`library.rescan`, Library menu and toolbar button) rescans every folder as one `rescan` job, so a file moved from one folder to another keeps its item (DNEW-5).
- **Rescan** in the Indexed folders dialog rescans the selected folder. Its new files are paired with what disappeared in it and with items already Missing anywhere in the library.
- `StartRescanCommand` refuses to start while import, rescan or removal work is unfinished for the folder (a library rescan: for any folder), as `ImportFoldersCommand` does for imports. A folder that can't be reached is marked Offline by the rescan itself (D188), and its items stay as they are.

## What a rescan does

| Stage | Pool | Does |
|-------|------|------|
| `rescan.reconcile` | IO | Lists the folders with their rules (`IFolderDiscovery.SnapshotAsync`), classifies each item as New / Unchanged / Modified / Disappeared by path (`FolderComparison`), pairs new files with disappeared items (`MoveDetector`), applies the result and stores the counts as the job's result. |
| `rescan.check` | IO | For a modified or returning file: fingerprints it, and decides whether its content changed (DNEW-3, DNEW-6). |
| `rescan.media` | CPU | Refreshes a changed file's metadata and thumbnails (DNEW-4) and publishes `MediaItemRegistered` with `ContentChanged`. |

Moves are paired on fingerprint and size, and confirmed by the content hash when the item has one (DNEW-2). Items under an unreadable subfolder or a skipped link, and excluded items, are left alone.

A partial folder (D012) is checked only for the files it tracks (`docs/file-discovery.md`, "Partial folders"): it never has New files, so a file moved within it shows as Missing, and a tracked file that can't be looked up is left alone.

**Retrying failed items:** a failed item whose file changed goes through `rescan.check` and `rescan.media` like any changed file, so replacing a corrupt file and rescanning processes it. A damaged file that hasn't changed stays failed. The failed work item of the old import job stays as history; items, not work items, are what a retry looks at (`RetryFailedItemsCommand`).

## Summary

When the job finishes, a message says how many files are new, moved or renamed, missing and updated, and how many folders couldn't be reached (`RescanSummaryText`). The counts of the reconciliation are the job's `Result` (JSON of `RescanCounts`, library schema version 8); "updated" is the number of the job's completed `rescan.media` work items, which only exist once the check found a change. `GetRescanSummaryQuery` answers `null` until the job has finished. Notifications (E15) replace the message window.

## Missing items

Missing items keep their metadata and thumbnails, show the status Missing in the details panel, and can be listed with `MediaItemFilter.Status` (the filter control comes with E10). **Remove missing items** (`library.remove-missing`, Library menu) asks first, with the number of items, then deletes every Missing item with its thumbnails in batches of 500 (`RemoveMissingItemsCommand`). The tags, favorites and other metadata of those items are lost; files on disk aren't touched.
