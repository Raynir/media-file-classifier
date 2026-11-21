# Responsive layout (E13-S07)

The main window has three layouts, chosen by its width in device-independent pixels (`LayoutBreakpoints`, `ShellLayout.UpdateWidth`). The window's minimum size is 720 × 480.

| Layout | Width | Navigation panel | Details panel |
|---|---|---|---|
| Wide | 1100 and up | Docked, as saved | Docked, as saved |
| Medium | 800 to 1099 | Icon rail. Its expand button opens the full panel as a drawer over the rail | Drawer |
| Narrow | under 800 | Drawer | Drawer |

- **Drawers** lie over the shell, on the panel's own side, above a scrim. A click on the scrim or Escape closes the open drawer, and so does choosing a navigation entry. Only one drawer is open at a time. The browser is never resized.
- **Open buttons** are in the browser's header row: one for each panel that isn't on screen (a closed drawer, or a panel hidden in the wide layout).
- **Saved state.** The saved layout (visible, collapsed, width) is the wide one. The layouts below it don't change it, and neither does opening or closing a drawer. Drawers close when the width crosses a breakpoint.
- **Toolbar.** Its own width logic moves actions into the overflow menu (E13-S03). That already follows the browser's width, so it needs no breakpoint.
- **For the phone layout (E20):** the narrow layout is the base. A panel's `PanelMode.Drawer` is what an Android head uses, and the open buttons are the entry points.

## Reachability at 720 × 480 (MFC-743)

Every command is in the main menu (`ShellCommandsTests.EveryCommand_IsReachableFromTheMainMenu`), and the menu bar fits at the minimum width (`ShellViewTests.AtTheMinimumWidth_TheMenuFitsAndBothPanelsCanBeOpened`). The rest of the UI is checked with `tools/ui-smoke/minimum-width.smoke` and `tools/ui-smoke/responsive.smoke`.

| Area | How it's reached at 720 × 480 | Evidence |
|---|---|---|
| Menu: Library, Edit, View, Item, Tools, Help (every command, with its shortcut) | The menu bar, always on top | Test above; shot `01-minimum-browser` |
| Search, similar-image search, thumbnail size | Toolbar | `01-minimum-browser` |
| Add folder, Import files, Rescan | Toolbar, then its overflow `…` menu (and the Library menu) | `01-minimum-browser`; menu commands |
| Navigation: All media, Recently imported, Favorites, folders, library list | Navigation drawer, from the button at the left of the browser header (`view.toggle-navigation`) | `02-minimum-navigation`; the smoke script picks an entry and the drawer closes |
| Filters, sort, grid or list view | The browser's header row (chips, Filters, sort, view buttons) | `01-minimum-browser`, `04-minimum-list` |
| Items: select, open, favourite, reveal, remove, find similar | The browser and the Item menu | Smoke script commands `item.favorite`, `edit.select-none` |
| Details, metadata editing, preview and video player | Details drawer: the button at the right of the browser header, or the `view.details-expanded` command | `03-minimum-details` |
| Folders dialog, settings, processing queue, library info, maintenance | Menus (Library, Tools, View) | `05-minimum-folders-dialog` |
| Status bar, toasts | Bottom of the window and above it | All shots |

Observation: at 480 high, the banner (while there's no model), the toolbar and the header row leave about a third of the window for the grid, which scrolls. It's usable but cramped; a shorter toolbar at small heights is a candidate for E20.
