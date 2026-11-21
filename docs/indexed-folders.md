# Indexed folders

How the user chooses which folders a library indexes (E02-S03): adding, merging, excluding, removing, and what happens when a folder can't be reached. The use cases are in `UseCases/Libraries`; the dialog is `IndexedFoldersViewModel` (Library › Manage folders…).

## The folders dialog

A modal dialog lists the open library's folders with their state (Online, Offline or Scanning) and item counts, and refreshes every 2 seconds while it's open, so states and counts follow an import. It's provisional (D095): Figma has no screen for it, and E02-S03-T00 designs the real one.

- **Add folder…** picks a folder and adds it. Each new folder is imported at once (`docs/import-pipeline.md`).
- **Remove** removes the selected folder, after a confirmation in the dialog that shows its item count.
- **Rules** of the selected folder: hidden files, cloud placeholders and excluded subfolders, saved with **Apply**.
- Merging and removing are confirmed in a bar inside the dialog, not in a second window.

The create-library dialog adds folders with the same rules, without asking: the new library has no items to merge.

## Overlapping folders

`IMediaFileSource.GetPathInside` compares folder handles without reading anything: full paths on Windows, case-insensitive. On Android, tree URIs aren't compared yet, so only the same folder twice is found (E20).

| Picked folder | Result |
|---------------|--------|
| Indexed already | Refused |
| Inside an indexed folder | Refused, naming the folder that covers it |
| Contains indexed folders | Asks, then merges them into the new folder |

**Merging** (`IFolderRepository.MergeIntoAsync`) runs in one transaction: the child's items move to the new folder with their paths prefixed by the child's place in it, keeping their IDs, so their metadata, tags, thumbnails and user values stay. The child's excluded subfolders and patterns are rewritten for the parent (`FolderRules.ExcludePatternsUnder`); its other rules give way to the parent's. The child's unfinished jobs are cancelled, and the full hashes they didn't make are queued for the parent. The parent's import then finds every file it already knows, so nothing is registered twice.

Folders being removed don't count as overlaps. "Selected files only" folders (D012) don't exist yet; absorbing them comes with E03-S05, and the merge already moves any folder's items.

## Partial folders

A partial folder (`IndexedFolder.SelectedFilesOnly`, "selected files only", D012, library schema 15) tracks only the files the user imported from it by selecting them (E03-S05, `docs/import-pipeline.md`): its items. Its import and its rescans look up those files alone, so other files in the folder are ignored (`docs/file-discovery.md`, "Partial folders"). It has no rules. A picked file in one of its subfolders joins it, with a path such as `2024/b.jpg`.

## Exclusions

Each folder's rules are its `FolderRules` (D142):

- **Excluded subfolders** are root-anchored exclude patterns: the user picks a folder inside the indexed folder, stored as `/2024/raw` (`FolderRules.ForSubfolder`). A leading `/` anchors a one-segment pattern at the root, as a pattern with any `/` already was.
- **Hidden files and folders** are skipped unless the toggle is on (the default is off).
- **Cloud placeholders** are skipped unless the toggle is on (`docs/file-discovery.md`).

Applying changed rules (`ChangeFolderRulesCommand`):

1. Cancels the folder's running import, which uses the old rules.
2. Marks the items the patterns now exclude as **Excluded** (D048), keeping their user values, and deletes their thumbnails. Files aren't touched.
3. Imports the folder again. Discovery finds the files the old rules skipped, and queues the Excluded items the new rules include: their thumbnails are made again and they go back to Pending, with their user values.

Turning the hidden toggle off doesn't mark hidden items Excluded yet, because the items don't record whether their file is hidden; the rescan (E02-S04) can.

## Removing a folder

`RemoveFolderCommand` cancels the folder's work and queues a `folder-remove` job at user priority. Its stage deletes the folder's items 500 at a time, with everything that belongs to them (metadata, runs, tags, keyframes, embeddings, collection memberships) and their thumbnails, then the folder. Files on disk are never touched. The folder disappears from the list at once; an interrupted removal carries on when the library opens again. Without the job system the folder is removed before the command returns.

## Offline folders

A folder is **Offline** when its root can't be listed: the drive or share is gone, the folder was deleted or moved, or access is denied (`IMediaFileSource.IsAvailableAsync`). `CheckFolderAvailabilityCommand` checks the folders and updates their status; it runs when a library opens, when the folders dialog opens, and for each folder before it's imported, where an Offline folder isn't queued. A folder goes back Online as soon as a check succeeds. There's no polling: live monitoring is E04.

Offline items stay as they are, with their metadata, and are never marked Missing because their root can't be reached (D025). Showing them greyed out is E14-S07.

Work queued for an item in an Offline folder (import stages, rescan checks, analysis) doesn't run against the missing file (MFC-1004). The stage throws `FolderOfflineException` before it touches the file, and the scheduler parks the work item without using a retry or marking the item failed. When a stage finds a file's directory gone, it checks the folder's root: if the root can't be listed, the folder is marked Offline and the work is parked the same way; if it can, the file is gone and the work fails at once as a permanent failure. Parked work is released when a check or a rescan finds a folder Online again, and when the library opens; work whose folder is still Offline parks again.
