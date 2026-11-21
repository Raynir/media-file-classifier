# Toolbar

The toolbar (E13-S03) fills the shell's Toolbar region above the media browser and the details panel (D108) while a library is open, and takes no space otherwise. It's one row, 72px high. The code is in `Ui/Toolbar` and `Ui/Views/Toolbar/ToolbarView.axaml`. It's provisional (D095), after Figma 25:378: 30px from the sides, controls 34px high.

## Slots

From left to right, in this order at every window width:

| Slot | Filled by | Width |
|------|-----------|-------|
| Search | The search bar with the similarity and reference image chips (E09-S01, E09-S03, E09-S04) | Fills what the others leave, and at least `Size.Toolbar.SearchMin` (160) |
| Filter | The filter button and the active filter chips (E10-S02) | As wide as its view |
| Sort | Empty: the sort control (E10-S04) sits at the end of the results header, next to the filters, as in the wireframes | — |
| View | The thumbnail size slider (E14-S01-T05, `ThumbnailSizeView`), and later the grid/list toggle (E14-S02) | As wide as its view |

- **Filling a slot:** a feature implements `IToolbarContributor`, registers it with `TryAddEnumerable` in its module, and in `Register` sets the slot (`toolbar.Search = ...`) to a view model. The toolbar shows the view that `ViewLocator` maps to it, so register the view there too. An empty slot takes no space, except Search, which is the free space.
- **Layout:** `ToolbarLayoutPanel` lays the row out. Unlike a grid with auto columns, it offers the actions only the width the slots leave, so the actions move into the overflow menu instead of pushing the slots out of the window.

## Actions

After the slots come the action buttons. Each runs an application command (E13-S05), so its label, icon, enablement and tooltip match the menus: the tooltip gives the shortcut, e.g. "Add folder… (Ctrl+Shift+O)", or why the command can't run. A command the platform doesn't support (`AppCommand.IsVisible` false) gets no button.

| Action | Command | Order | Priority | Added by |
|--------|---------|-------|----------|----------|
| Add folder… | `library.add-folder` (Ctrl+Shift+O): pick a folder, then the Indexed folders dialog opens with it added, as when a folder is dropped on the navigation panel | 100 | 100 | `LibraryToolbar` |
| Import files… | `library.import-files` (Ctrl+Shift+I): pick media files and import them, then a message says what happened (E03-S05) | 105 | 95 | `LibraryToolbar` |
| Rescan library | `library.rescan`: rescan every folder, then show a summary message (E02-S04) | 110 | 90 | `LibraryToolbar` |

- **Adding an action:** in an `IToolbarContributor`, call `toolbar.AddAction(commands[id], order, priority)`. Order places it left to right. Priority decides which actions move into the overflow menu first as the window narrows: the lowest. Analyze pending (E06-S06) comes this way.

## Overflow menu

When the window narrows, the Search slot shrinks first, down to its minimum. Then the actions that no longer fit move into a More menu (⋯, "More actions") at the end of the row: the lowest priority first, and of equal priorities the rightmost first (`ToolbarOverflow.Fit`). The menu lists them in their toolbar order, as menu items bound to the same commands, with their shortcuts and disabled reasons. When the window widens, they come back. The slots never move into the menu, so no action becomes unreachable at the minimum window width (720).

- **How:** `ToolbarActionPanel`, the action area's items panel, measures every button, decides what fits, and sets `ToolbarAction.IsOverflowed`. The view hides those buttons, and `ToolbarOverflowMenu` lists their commands. A hidden button measures as zero wide, so the panel remembers each button's width from when it was last shown.
- **Responsive layout (E13-S07):** reuse `ToolbarOverflowMenu` and the panel; don't add a second menu.
