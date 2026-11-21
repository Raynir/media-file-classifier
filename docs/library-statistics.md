# Library statistics (E02-S05)

How big the open library is and how far indexing has got. The layout was approved by the user on 2026-11-21
(MFC-298); there's no Figma mockup for it, so it follows the existing app styles and is provisional (D095). The full
dashboard comes later (E15).

## Where it shows

- **Status bar summary** at the right end, outermost (after the processing indicator and the offline and provider
  notices): "12,345 in library · 340 pending". The pending part is left out at 0. It isn't shown while no library is
  open. Its tooltip lists the statuses ("11,900 indexed, 340 pending, 80 with errors, 25 missing. Click for library
  information."). Clicking it opens the info window. "in library" keeps it apart from the result count of the
  current view at the left end.
- **Library information window**: a small non-modal window owned by the main window, one at a time, like the
  processing queue. Opened from the summary segment and from **View › Library information**. Its content is a
  self-contained view (`LibraryInfoView`), so the Settings "Library" page hosts it too (MFC-133, `LibraryInformationSectionViewModel`, which adds the library location with Reveal and Copy path).

## Counting rules

Excluded items never count (MFC-123).

| Figure | Counts |
|---|---|
| Total | every item that isn't Excluded |
| Images / Animated images / Videos | the total by media type |
| Indexed | status Indexed |
| Pending | status Discovered or Pending (both still to do) |
| Errors | status Error |
| Missing | status Missing |
| Media files | the summed file sizes of items whose file is there: every item except Excluded and Missing |
| Database | `library.db` plus its `-wal` and `-shm` files |
| Thumbnails | every file under `thumbnails/`, keyframe pictures included |
| Backups | every file under `backups/` |
| Library total | Database + Thumbnails + Backups, i.e. the `.mfc` folder |

The counts come from one aggregate query (no stored counters, no schema change), refreshed at most once a second
while items change. Disk usage is measured in the background when the window opens ("Calculating…" until then),
cached per library, and measured again at most every 30 seconds while the window is open.

## Window

Title "Library information", with the library name under it. Three groups of label/value rows, values right-aligned
with digit grouping; sizes as "85.2 MB" / "1.21 GB".

```
Library information
Photos
ITEMS
Total                     12,345
Images                    10,000
Animated images              345
Videos                     2,000
STATUS
Indexed                   11,900
Pending                      340
Errors                        80
Missing                       25
DISK USAGE
Media files             120.4 GB
Database                 85.2 MB
Thumbnails               1.21 GB
Backups                  40.0 MB
Library total            1.33 GB
                         [ Close ]
```

States:
- **Small library**: the same rows; zeros show as 0, so the layout doesn't jump.
- **Large library**: digit grouping; the window is wide enough for "100,000,000" and "999.9 GB".
- **Disk usage loading**: "Calculating…" in the four disk rows; the item rows show at once.
- **No library open**: "Open a library to see its statistics." and nothing else.
- **Light and dark theme**: theme brushes only, so both work.

Keyboard and accessibility: the rows are read-only text; each row's accessible name is "Images: 10,000". Focus starts
on Close (the default and cancel button); Escape closes the window.

## Final English UI text

| Key | Text |
|---|---|
| Window title, command | Library information |
| Group headers | Items, Status, Disk usage |
| Rows | Total, Images, Animated images, Videos, Indexed, Pending, Errors, Missing, Media files, Database, Thumbnails, Backups, Library total |
| Loading | Calculating… |
| No library | Open a library to see its statistics. |
| Status bar | {total} in library · {pending} pending |
| Status bar tooltip | {indexed} indexed, {pending} pending, {errors} with errors, {missing} missing. Click for library information. |
