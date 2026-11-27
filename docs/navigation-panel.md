# Navigation panel

The panel on the left of the main window (E13-S02) fills the shell's Navigation region (D111) while a library is open. It shows sections of entries, and selecting an entry sets the media browser's scope. The code is in `Ui/Navigation` and `Ui/ViewModels/Navigation/NavigationPanelViewModel.cs`. It's provisional (D095), after Figma 25:133 and the collapsed rail 30:3094.

## Sections and entries

| Section | Entries | Count |
|---------|---------|-------|
| Library | All media, Recently imported (the last 7 days, D053), Favorites (E11-S01), Duplicates (E12-S05-T03) | Items, Excluded ones not counted; Duplicates counts open groups and hides the count at 0 |
| Folders | The indexed folders and, below each, its subfolders | Items in the folder and everything inside it |

- **Adding a section:** another Epic implements `INavigationContributor` and registers it with `TryAddEnumerable` in its module. The contributor creates its `NavigationSection` once and updates its entries in `RefreshAsync`, using `NavigationEntry.Merge` so the existing entries keep their expanded and selected state. Pending review (E08-S01), Collections, Saved searches and the library switcher come this way. Sections are ordered by `Order`: Library is 0 and Folders is 100.
- **Entries:** each has a label, an icon (a resource key), an optional count, an Offline flag, and the `BrowseScope` it selects. An entry with a child loader shows an expander, and its children load the first time it's expanded.
- **Section header:** a section can have an action (Folders: Manage folders…) and accept dropped folders (Folders: they're added through the folders dialog, `docs/indexed-folders.md`). Dropping anywhere on the panel goes to Folders.

## Scopes

An entry can have an `Open` action instead of a scope: Duplicates opens its page in the browser's place through `BrowserPages`, and picking a scope entry or opening another library brings the browser back (`docs/duplicate-review.md`).

`BrowseScope` is what the browser shows: `AllItemsScope`, `RecentlyImportedScope`, `FavoritesScope` (items with `IsFavorite`) or `FolderScope(folder, subfolder)`. Search and filters (E09) apply within it. `LibraryItemsSource.Scope` turns it into a `MediaItemFilter` for the item queries, and changing it reloads the grid.

- Items marked Excluded aren't shown in any scope (D048, D024).
- A subfolder scope includes everything inside the subfolder. It's matched on the stored relative path with `LIKE 'sub/%'`, with `%` and `_` escaped.
- Opening a library selects All media, and the selection isn't saved. If the selected folder goes away, All media is selected again.

## Folders tree

Subfolders come from the items' paths in the library (`IMediaItemRepository.GetSubfoldersAsync`), not from the disk. That means the tree works for Offline folders and lists only subfolders that hold media. A subfolder with no media, or one that isn't scanned yet, doesn't appear. An Offline folder shows the offline icon, and the tooltip says so. Its items stay browsable (D025).

## Live counts

The panel watches the open library's `IMediaItemRepository.Changed`. That event is raised after each write or batch, so during an import it fires often. The first change starts a one-second timer, and when the timer fires every contributor refreshes once, covering all the changes made in that second. The counts are grouped COUNT queries on a read connection, so the UI thread only awaits them. The panel also refreshes when a library opens, again a second later (after the open-time folder check), and when the folders dialog closes.

## Collapsed and saved state

- **Collapsed panel:** the panel becomes a 66px icon rail (D108). It shows one icon per top-level entry, with an offline mark where needed and a divider between sections. The selected entry has the Figma selection fill and border, and tooltips show the label and count.
- **Collapsed sections:** their keys are saved with the window layout (`MainWindowLayout.CollapsedNavigationSections` in `settings.json`), so they're the same across libraries. View › Reset layout expands them again.
