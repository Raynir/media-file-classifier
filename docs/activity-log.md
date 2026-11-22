# Activity log

What the app did to the library, listed newest first (E15-S04). Open it from **View > Activity log**; it is a window of its own, not modal, so work goes on beside it.

## What is recorded

One event per finished operation, written to the library (table `activity_event`, schema 17):

| Type | Recorded when | Link |
|---|---|---|
| `Import`, `Rescan`, `Analysis`, `Maintenance` (thumbnails) | A job of that kind finishes (`ActivityJobRecorder`, from `IJobProgress.JobFinished`). A job that handled no items records nothing. | Its folder |
| `FolderAdded`, `FolderRemoved` | A folder is added or removed (also with the items that went with it). | The folder, while it exists |
| `BulkEdit` | An edit of more than one item, and its undo. | None |
| `DuplicateResolution`, `Export`, `LibraryImport` | Defined, not recorded yet: those features do not exist. | |
| `FolderMonitoring` | Defined for folder monitoring (MFC-140) to record. | |

A feature records an event with `RecordActivityCommand`. It never throws: a log that cannot be written must not fail the work it describes.

An event stores its **type** and **named counts** (`ActivityCounts`), not text. The window words it from the UI strings (`Activity_Title_<type>`, `Activity_Count_<name>`), so it follows the UI language. A count the window does not know is left out; a missing one reads as zero.

## Retention

The newest **5,000** events are kept (`ActivityRetention.MaxEntries`). The oldest are removed in the same write that adds a new one.

## The window

- Rows have two lines: a bold summary, then the counts and the folder. The time of day is at the right. Rows are grouped under **Today**, **Yesterday** and dated headers.
- **Chips** filter by kind: All, Imports, Rescans, Analysis, Edits, Folders (added, removed, monitoring), Duplicates, Exports (export and library import) and Errors (warnings and errors). Each opening starts on All.
- **Show items** selects the event's folder in the navigation panel and brings the main window forward. It is hidden when the event has no folder or the folder is gone.
- The list loads 100 events at a time, and more when it scrolls near the end (or with **Load older events**). It is virtualised.
- It refreshes by itself when an event is recorded (`ActivityChanges.Changed`), and keeps the scroll position.
- Empty log and a filter that matches nothing each have their own message.

## Code

| What | Where |
|---|---|
| Event model | `Domain/Activity` |
| Repository | `IActivityRepository`, `ActivityRepository` (paging by skip and take, filter by types and minimum severity) |
| Recording | `UseCases/Activity` (`RecordActivityCommand`, `ActivityJobRecorder`, `ActivityChanges`) |
| Page query | `UseCases/Activity/GetActivityPage` (adds the folder a link leads to) |
| Window | `Ui/Activity` (presenter, command `view.activity-log`, wording), `ViewModels/Activity`, `Views/Activity` |

## Checking it

`tools/ui-smoke/activity-log.smoke` seeds events (`seed-activity`) and screenshots the window in both themes (docs/testing.md, "Real-app checks").
