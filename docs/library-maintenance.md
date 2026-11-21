# Library maintenance (E02-S06)

Three repair actions for when search results or thumbnails look wrong, or the library may be damaged. None of them
changes metadata or embeddings, and none starts AI analysis (that's E06). The layout was approved by the user on
2026-11-21 (MFC-313); there's no Figma mockup for it.

## Where they are

The three actions are commands in the Library command group, so they show in the Library menu and the command
palette, after Retry failed items. They are `AppCommand`s, so the Settings "Library" page (MFC-133) can show them as
buttons too. Each is disabled with "Open a library first" when no library is open.

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

"All" deletes each item's own thumbnail files (all sizes) and keeps the keyframe pictures of video strips, which only
AI analysis makes. Items that are missing, excluded or not imported yet are skipped. A failure leaves the item's
placeholder and doesn't mark the item as failed.

## Integrity report

The report is a message window. States and texts:

- **Healthy:** "The library is in good health. No problems were found."
- **Corruption found, backup available:** "The library database is damaged: SQLite found N problems. Search,
  browsing and saving changes may fail.\n\nA backup from {date} is available. Restoring it brings back the library as
  it was then: changes made after that date are lost. The damaged database is kept in the backups folder." Buttons:
  **Restore backup** / **Not now**.
- **Corruption found, no backup:** "The library database is damaged: SQLite found N problems. Search, browsing and
  saving changes may fail.\n\nThere's no backup to restore." Button: **Close**.
- **Restore done:** "The backup from {date} was restored and passed the integrity check. Changes made after that date
  are lost." Button: **Close**.
- **Restore failed:** "The backup couldn't be restored: {reason}. The library wasn't changed." Button: **Close**.

The first SQLite messages go to the log, not into the report.

## Restoring the backup

The only backup is the one made before the last schema migration (`backups/library-v<N>-<time>.db`, E01-S03-T05);
user backups are later (E02-S09). The restore:

1. checks the backup with `PRAGMA integrity_check`, and stops if it's damaged too;
2. stops the jobs and closes the library;
3. moves the damaged database to `backups/library-damaged-<time>.db` and removes its WAL files;
4. copies the backup in and opens the library again, which migrates it to the current schema;
5. runs the integrity check on the reopened library and reports the result.
