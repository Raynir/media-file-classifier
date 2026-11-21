# Drag-and-drop import (E03-S06)

Dropping files and folders from Windows File Explorer on the main window imports them. The design was agreed with the user on 2026-11-21 (MFC-658), since no mockup exists.

## Flow

1. The user drags items over the main window. If a library is open, the whole window dims and a centered card shows a drop icon, **Drop to import** and the hint *Files are imported. Folders are added to the library.* If no library is open, nothing shows and the cursor says the drop isn't allowed.
2. Over the search bar the overlay hides: a drop there is a search by image (E06), never an import.
3. On drop, files go to `ImportFilesCommand` (silently, no dialog) and folders to the indexed folders dialog, which asks the overlap and merge questions (E02-S03). The dialog opens only after the drop callback returns, so the app the items came from isn't blocked.
4. When the dialog is closed (or at once for files only) one toast sums up: files being imported, already in the library, left out by rules, not supported, can't be read, and folders added. It follows the "Show informational notifications" setting (D435).

## States

| State | Overlay | Cursor |
|---|---|---|
| Dragging files or folders, library open | Dim + card | Copy |
| Dragging, no library open | None | Not allowed |
| Dragging over the search bar | None | Copy for pictures, otherwise not allowed |
| Unsupported items (e.g. `.txt`) | Same card: the format is known only when importing | Copy; the toast says "N files aren't a supported format." |

Light and dark use the existing theme tokens, so the card follows the theme.

## Keyboard and accessibility

Dragging is pointer-only; the keyboard route to the same result stays Import files (`Ctrl+Shift+I`) and Add folder (`Ctrl+Shift+O`). The overlay isn't focusable. Its card has the accessible name "Drop to import".

## Text

- Title: `Drop to import`
- Hint: `Files are imported. Folders are added to the library.`
- Toast lines: the import lines of E03-S05, plus `1 folder added.` or `N folders added.`
