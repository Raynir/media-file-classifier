# Toolbar

The toolbar (E13-S03) fills the shell's Toolbar region above the media browser and the details panel (D108) while a library is open, and takes no space otherwise. It's one row, 72px high. The code is in `Ui/Toolbar` and `Ui/Views/Toolbar/ToolbarView.axaml`. It's provisional (D095), after Figma 25:378: 30px from the sides, controls 34px high.

## Slots

From left to right, in this order at every window width:

| Slot | Filled by | Width |
|------|-----------|-------|
| Search | The search bar with the similarity and reference image chips (E09-S01, E09-S03, E09-S04) | Fills what the others leave, and at least `Size.Toolbar.SearchMin` (160) |
| Filter | The filter button and the active filter chips (E10-S02) | As wide as its view |
| Sort | The sort control (E10-S04) | As wide as its view |
| View | The thumbnail size slider (E14-S01-T05, `ThumbnailSizeView`), and later the grid/list toggle (E14-S02) | As wide as its view |

- **Filling a slot:** a feature implements `IToolbarContributor`, registers it with `TryAddEnumerable` in its module, and in `Register` sets the slot (`toolbar.Search = ...`) to a view model. The toolbar shows the view that `ViewLocator` maps to it, so register the view there too. An empty slot takes no space, except Search, which is the free space.
- **Layout:** `ToolbarLayoutPanel` lays the row out. Unlike a grid with auto columns, it offers the actions only the width the slots leave, so the actions move into the overflow menu instead of pushing the slots out of the window.
