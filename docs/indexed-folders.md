# Indexed folders

How the user chooses which folders a library indexes (E02-S03): adding, merging, excluding, removing, and what happens when a folder can't be reached. The use cases are in `UseCases/Libraries`; the dialog is `IndexedFoldersViewModel` (Library › Manage folders…).

## The folders dialog

A modal dialog lists the open library's folders with their state (Online, Offline or Scanning) and item counts, and refreshes every 2 seconds while it's open, so states and counts follow an import. It's provisional (D095): Figma has no screen for it, and E02-S03-T00 designs the real one.

- **Add folder…** picks a folder and adds it. Each new folder is imported at once (`docs/import-pipeline.md`).
- **Remove** removes the selected folder, after a confirmation in the dialog that shows its item count.
- **Monitoring** of the selected folder (MFC-1057): a drop-down with Auto, Watch, Poll and Manual that sends `ChangeMonitoringModeCommand` at once (no Apply). Below it, a sentence on the mode (for Auto, the mode it resolves to), the next check of a polled folder, the cloud client that syncs it, and, for a network share set to Watch, a hint with a "Use Poll" button. The badges and the picker come from `GetFolderMonitoringQuery`, read on every 2 s refresh, when the watchers' state changes and after a pick.
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

**Index whole folder** (`IndexWholeFolderCommand`, E03-S05-T03) makes it a full folder: it cancels the folder's running import, clears `SelectedFilesOnly` and imports the folder. Full discovery knows the existing items by their paths, so they keep their IDs and metadata, and the other files are imported under the folder's rules. The folders dialog tags a partial folder "Selected files only" and shows the note and the button in place of its rules; the button asks in the dialog's confirmation bar first.

Add folder and partial folders (`IndexedFolders.FindOverlap`):

- Adding the partial folder itself (`FolderOverlapKind.PartialFolder`) indexes it whole; the dialog asks the same question as the button.
- Adding a folder inside it (`InsidePartial`, when no full folder covers it) is allowed: the new folder is added, and the partial folder's items under it move to it with the prefix stripped from their paths, keeping IDs and metadata (`IFolderRepository.SplitIntoAsync`). The partial folder is deleted if it has no items left, otherwise its import is started again. Paths compare without case, like Windows handles.
- A full folder that contains a partial folder merges it, as with any folder it contains.

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

A folder is **Offline** when its root can't be listed: the drive or share is gone, the folder was deleted or moved, or access is denied (`IMediaFileSource.IsAvailableAsync`). `CheckFolderAvailabilityCommand` checks the folders and updates their status; it runs when a library opens, when the folders dialog opens, and for each folder before it's imported, where an Offline folder isn't queued. A folder goes back Online as soon as a check succeeds.

Statuses follow drives and shares live (E14-S07-T04, amends D188). `FolderAvailabilityMonitor`, started by the composition root, checks every folder 1 s after volume events go quiet (`IVolumeWatcher`: on Windows `WindowsVolumeWatcher` hooks the main window for `WM_DEVICECHANGE` volume arrival and removal, so USB drives, memory cards and mapped network drives count; other platforms get the no-op `NoVolumeWatcher`). Every 30 s it also checks the Offline folders alone (`CheckFolderAvailabilityCommand.OfflineOnly`), because a share opened by UNC path raises no event; Online folders aren't polled. Checks don't overlap: a poll that finds one running is skipped. When a check changes a status, `FolderStatusChanges` refreshes the results (and through them the details panel) and the status bar's offline notice, so a reconnected drive's items stop being dimmed on their own.

Offline items stay as they are, with their metadata, and are never marked Missing because their root can't be reached (D025). The browser shows them dimmed with an Offline badge (E14-S07). The filter panel's "Hide offline items" check box (`HideOfflineFilter`, id `hide-offline`, off by default) leaves them out in every search mode, and is saved with the filter state and saved searches like any filter.

Each folder stores the label of the volume it's on (`IndexedFolder.VolumeLabel`, library schema 16, `IMediaFileSource.GetVolumeLabelAsync`). It's read when the folder is added and again on every availability check that finds it Online, and kept while it's Offline, so the badge, tooltip and details panel can name the drive to connect: "Offline: drive 'Backup-2TB' (E:\Photos)". An unlabelled drive is named by its letter and a network share by `\\server\share`; on Android, which has no labels, only the folder is named.

### Drive connect and disconnect (E04-S04)

- **Eject:** a watched folder holds its root open, which would make Windows refuse "Safely remove" as "in use". `EjectAwareFolderWatcherFactory` wraps the watcher factory on desktop: each watcher registers its folder with `IEjectNotifications` (`WindowsEjectNotifications` opens the folder and registers it for `DBT_DEVTYP_HANDLE` notifications to the main window). On `DBT_DEVICEQUERYREMOVE` the watcher and the registered handle are closed before the message returns, so the eject goes ahead; the removal then marks the folder Offline as usual. If the eject is cancelled (`DBT_DEVICEQUERYREMOVEFAILED`), the folder is watched again and the watcher reports lost events, so it's rescanned (D517).
- **Going away:** nothing is marked Missing. The watcher drops a batch with deletions when the folder's root can't be reached and checks the folder instead (rescanning it if the check finds it still Online, MFC-1047), and a rescan whose listing finds files gone checks the root again before trusting it; either way the folder becomes Offline.
- **Coming back:** a check that brings folders back Online (including a relink below) publishes `FoldersReconnected`; their watchers restart through `StatusChanged`, and `ReconnectCatchUp` queues a quick sync (E04-S03, `StartRescanCommand(folderId, Quick: true)`) of each once its watcher runs, whatever the "quick sync on open" switch says. A start refused as `Busy` is asked for again every 15 s while the folder is still Online, watched and its library open.
- **Volume identity:** each folder stores the identity of its volume (`IndexedFolder.VolumeId`, library schema 18, `IVolumeLocator`; on Windows `WindowsVolumeLocator` reads the file system's serial number, e.g. `1A2B-3C4D`, which travels with the drive and changes only on a format). It's read when the folder is added (MFC-1045) and on every check that finds it Online, and kept while Offline; a write from a copy of the folder without one keeps the stored one (MFC-1046). Only the check stores the label and identity (`IFolderRepository.UpdateAvailabilityAndVolumeAsync`); discovery, rescans and imports store just the status, and a whole-row update from a copy without them keeps the stored ones (MFC-1055). Platforms without drive letters get the no-op `NoVolumeLocator`. Looking for a folder's drive under other letters skips drives that aren't ready, and every serial read runs with critical-error dialogs off, so an empty card reader or DVD drive never shows "No disk" (MFC-1048).
- **Another drive under the letter:** a folder whose root is reachable but on another volume than the stored one is Offline (checks, rescans and imports all use `FolderReachability`), so the other drive is never scanned as the folder.
- **Use this drive:** when that other drive is the folder's own drive with a new identity (cloned to a new disk, reformatted and restored), the user accepts it (MFC-1051). Selecting such an Offline folder in the folders dialog asks `FindOtherDriveQuery`, which lists the root and reads the drive there (again every 10 s while the folder stays Offline and selected, so a clone plugged in meanwhile is offered); when it isn't the stored one, the panel names it and offers "Use this drive", confirmed in the dialog. Every other Offline folder from the same old drive whose root leads to the same new drive takes it too, and the confirmation says how many. `UseThisDriveCommand` reads the drives again, stores each folder's new identity and label (`RelinkAsync` to the same root, so a folder relinked or removed meanwhile is left alone), brings the folders Online with their items, records "Folder's drive replaced" (`ActivityEventType.FolderDriveReplaced`) in the activity log, releases parked work and publishes `FoldersReconnected`, so the usual quick sync runs: files that aren't on the drive are marked Missing. Mapped network drives have no identity (`WindowsVolumeLocator` returns none for drives Windows reports as remote, as for UNC paths), so a NAS failover or a new server doesn't make their folders Offline.
- **New drive letter:** an Offline folder whose volume is mounted under exactly one other drive root, with the folder present there, follows it: its paths are rewritten (items keep their relative paths), it's Online, and the activity log records "Folder moved to a new drive letter" (`ActivityEventType.FolderRelinked`). Not when the new root is another folder of the library or overlaps one; then it stays Offline. `IFolderRepository.RelinkAsync` stores only the paths, availability and volume, and only if the folder still has its old root; a check whose relink is refused that way leaves the folder as the newer write stored it (MFC-1044).

Work queued for an item in an Offline folder (import stages, rescan checks, analysis) doesn't run against the missing file (MFC-1004). The stage throws `FolderOfflineException` before it touches the file, and the scheduler parks the work item without using a retry or marking the item failed. When a stage finds a file's directory gone, it checks the folder's root: if the root can't be listed, the folder is marked Offline and the work is parked the same way; if it can, the file is gone and the work fails at once as a permanent failure. Parked work is released when a check or a rescan finds a folder Online again, and when the library opens; work whose folder is still Offline parks again.

### Monitoring controls and status (E04-S06)

- **Library switch:** `KnownSettings.MonitoringEnabled` (`monitoring.enabled`, library setting stored in `LibrarySettings`, no schema change). Off, `FolderWatchManager` watches nothing; turning it on again watches and queues a library quick sync.
- **State:** `FolderWatchManager.GetState` gives an Online folder one `FolderMonitoringState` (the same enum as `GetFolderMonitoringQuery`): Watching, Polling or Manual by its effective mode (E04-S05), Paused for a watched or polled folder while monitoring is paused, Error (it should be watched but isn't) or Off (monitoring is off for the library). The folder list adds a badge for it: Watching, Paused, Can't watch, Not monitored, Polling or Manual (Offline folders show only Offline). `StateChanged` is raised when it may have changed; the dialog reads it on its 2 s refresh.
- **Pause:** `MonitoringPause` (singleton, session only, not stored). Paused, watchers keep gathering but `ChangeProcessor` queues nothing (timer stopped, resume reschedules), and the rescans of lost events, library re-enabling and `ReconnectCatchUp` wait. Pausing all processing (E05-S05) is separate and holds the jobs already queued (D536). Buttons: the folders dialog and the status-bar notice.
- **Activity and notification:** `ApplyChangesStage` adds each batch's counts to `MonitoringTally` by job. `ActivityJobRecorder` records such an import job as one `FolderMonitoring` event; `JobNotifications` notifies only at `monitoring.notifyThreshold` changes (default 10), instead of the generic import notice.

## Monitoring modes (E04-S05)

Each folder has a monitoring mode (`IndexedFolder.MonitoringMode`, column `indexed_folder.monitoring_mode`, schema 19):

| Mode | What keeps the folder up to date |
| --- | --- |
| **Auto** (default) | Poll on a network share (a UNC path or a mapped network drive), Watch otherwise. Resolved at run time, so a remapped drive follows. |
| **Watch** | A live `FileSystemWatcher` (E04-S01). |
| **Poll** | A quick sync (`StartRescanCommand(folderId, Quick: true)`) every poll interval. For shares whose change notifications are unreliable. |
| **Manual** | Nothing runs on its own: no watcher, no poll, left out of the quick sync on open and of the reconnect catch-up. Only a rescan the user starts lists it. Leaving Manual for another mode queues one catch-up quick sync. |

- **Poll interval:** one app-wide setting, "Minutes between checks of polled folders" (`monitoring.pollIntervalMinutes`, General, 1–1440 minutes, default 15). `FolderPoller` keeps one timer for the earliest due folder; a folder is first polled one interval after it starts being polled. A poll refused as `Busy` (the folder's import or rescan runs) waits for the next interval. A shorter interval applies at once, a longer one from the next poll.
- **Where a folder is:** `IFolderLocationProbe` (`Infrastructure.Abstractions/Platform`) tells network and cloud without opening anything; `FolderLocations` caches the answer per Online folder. On Windows, `WindowsFolderLocationProbe` (Desktop) treats UNC paths and drives `GetDriveType` reports as remote as network; a folder under a sync root registered with the Windows cloud files API (`CfGetSyncRootInfoByPath`: OneDrive, Dropbox, iCloud, ...) is cloud-synced and named by its provider, and Google Drive for desktop is recognised by its volume label or file system name. Elsewhere `PathFolderLocationProbe` knows UNC paths only.
- **State for the UI:** `GetFolderMonitoringQuery` returns a `FolderMonitoringInfo` per folder: its `Mode`, the `EffectiveMode` it runs in, its `State` (the same values as `FolderWatchManager.GetState`, plus Offline), `IsNetwork`, `CloudProvider` and `NextPoll` (only while polled: null while paused, as a due poll waits for the resume). Query again on the `FolderMonitoringChanged` notification, raised when the watched or polled folders change, when monitoring is paused or resumed, after each round of polls and when the poll interval changes. `GetState` doesn't know the poller, so where the poller doesn't run (tests) a Poll folder is Polling there and Manual in the query; the folders dialog takes its badge from the query.
- **Switch and pause:** polls obey the library's monitoring switch (`monitoring.enabled`) like the watchers: with it off no folder is polled. While monitoring is paused (E04-S06) a due poll waits and runs once on resume. Pausing all processing (E05-S05) holds the quick syncs polls already queued (D536). `ChangeMonitoringModeCommand` sets a mode (`Changed`, `Unchanged` or `NotFound`); watchers and polls follow on their own.

### Cloud placeholders (E04-S05-T03, E03-S02-T04)

A cloud-only file (attributes `Offline`, `RecallOnOpen` or `RecallOnDataAccess`) is recognised from its directory entry without being opened. Unless the folder's "cloud placeholders" rule is on, it's never fingerprinted or read for metadata: discovery skips and counts it (`DiscoveryResult.CloudPlaceholdersSkipped`, and the import's log line), a rescan leaves a known item's placeholder alone, and the watcher drops its changes. When the file is made local ("Always keep on this device"), only its attributes change: the watcher listens for attribute changes (`NotifyFilters.Attributes`), so the file is imported then; a polled folder imports it at its next poll.
