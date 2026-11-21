# Library maintenance (E02-S06)

Three repair actions for when search results or thumbnails look wrong, or the library may be damaged. None of them
changes metadata or embeddings, and none starts AI analysis (that's E06). The layout was approved by the user on
2026-11-21 (MFC-313); there's no Figma mockup for it.

## Where they are

The three actions are commands in the Library command group, so they show in the Library menu and the command
palette, after Retry failed items. They are `AppCommand`s, so the Settings "Library" page shows them as
buttons too (MFC-133, `LibraryMaintenanceSectionViewModel`). Each is disabled with "Open a library first" when no library is open.

| Command | ID | What it does |
|---|---|---|
| Refresh search index | `library.refresh-index` | Rebuilds the keyword and vector indexes from the stored data. |
| Regenerate thumbnails… | `library.regenerate-thumbnails` | Asks which thumbnails, then queues a thumbnail job. |
| Check library integrity | `library.check-integrity` | Runs the database integrity check and shows a report. |

## Progress and cancellation

- **Refresh search index** and **Check library integrity** run in the background while the app stays usable. A
  status-bar segment shows "Refreshing the search index… 42%" or "Checking the library…". Clicking it asks
  "Stop refreshing the search index?" (Stop / Keep going). Only one of them runs at a time; starting another says
  "Another library task is running. Try again when it has finished." They aren't persisted: after a restart the user
  runs them again.
- **Regenerate thumbnails** is a job in the processing queue (job type `thumbnails`), with one work item per item, so
  it shows progress and can be cancelled there like any other job. Items on screen go first, through the grid's boost.

## Regenerate thumbnails dialog

Title "Regenerate thumbnails", focus order: the two options, Cancel, Regenerate (default button).

- (•) **Only missing or outdated** — "Makes the thumbnails that are missing or older than their file. Fast."
- ( ) **All** — "Deletes every thumbnail and makes it again. Takes longer."
- Note: "Items on screen go first. Descriptions, tags and AI results aren't changed."

"All" first deletes every item thumbnail file in the cache (all sizes, including files of items no longer in the
library) and keeps the keyframe pictures of video strips, which only AI analysis makes (user's choice, 2026-11-21). Then
it queues every item with the `all` payload, so a file that couldn't be deleted is still replaced. A cancelled run is
finished by "Only missing or outdated". Items that are missing, excluded or not imported yet are skipped. The stage
reads the stored technical metadata and writes only thumbnail files. A failure fails the work item (it shows in the
processing queue) but leaves the item's status and placeholder alone.

While an import or rescan is unfinished, both options say "Thumbnails can be regenerated when the import has
finished." and queue nothing: the import writes thumbnails itself, and "All" would delete them under it (user's choice,
2026-11-21).

The job runs at import priority: ahead of hashing and analysis, behind user actions. The grid's boost (E05-S01-T03)
moves the items on screen ahead of the rest.

## Integrity report

The report is a message window. States and texts:

- **Healthy:** "The library is in good health. No problems were found."
- **Corruption found, backup available:** "The library database is damaged. Search, browsing and saving changes may
  fail.\n\nA backup from {date} is available. Restoring it brings back the library as
  it was then: changes made after that date are lost. The damaged database is kept in the backups folder." Buttons:
  **Restore backup** / **Not now**.
- **Corruption found, no backup:** "The library database is damaged. Search, browsing and saving changes may fail.\n\nThere's no backup to restore." Button: **Close**.
- **Restore done:** "The backup from {date} was restored and passed the integrity check. Changes made after that date
  are lost." Button: **Close**.
- **Restore failed:** "The backup couldn't be restored: {reason}. The library wasn't changed." Button: **Close**.
  The reasons: "the backup is damaged too", "the library files couldn't be replaced", "the restored library couldn't
  be opened". In the last two cases the damaged database is put back and the library opened again.
- **Restored, still damaged** (the reopened library fails its check): "The backup from {date} was restored, but the
  integrity check still found problems. The damaged database is kept in the backups folder." Button: **Close**.
- **Couldn't put back** (the restored library didn't open and the damaged database couldn't be moved back): "The
  backup couldn't be restored, and the damaged database couldn't be put back. It's kept as {path}." Button: **Close**.

SQLite's messages (the first 20) go to the log, not into the report.

## Restoring the backup

The only backup is the one made before the last schema migration (`backups/library-v<N>-<time>.db`, E01-S03-T05);
user backups are later (E02-S09). The restore:

1. checks the backup with `PRAGMA integrity_check`, and stops if it's damaged too;
2. stops the jobs and closes the library;
3. copies the backup next to the database first (so a full disk fails before anything moves), then moves the damaged
   database and its `-wal` file to `backups/library-damaged-<time>.db[-wal]`, deletes `-shm` and renames the copy in;
   when a step fails, the moved files go back;
4. copies the backup in and opens the library again, which migrates it to the current schema;
5. runs the integrity check on the reopened library and reports the result.

The restore doesn't run on the maintenance runner, because closing the library cancels the runner's task; once the
library is closed the steps aren't cancelled.
