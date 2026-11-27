# Duplicate review and comparison (E12-S03)

The Duplicates view lists the open duplicate groups and compares the members of the selected one. It opens from the **Duplicates** entry of the navigation panel's Library section and takes the media browser's place in the main content area. Resolving, ignoring and the History tab come with MFC-144 part B.

## Opening it

`NavigationEntry` has an optional `Open` action instead of a `BrowseScope`. The Duplicates entry (`LibraryNavigation.DuplicatesKey`) is always shown; its count is the number of open groups from `GetDuplicateStatisticsQuery`, hidden while it is 0, and it follows the groups through `IDuplicateGroupRepository.Changed` (at most one refresh a second, like the other counts).

`BrowserPages` swaps `ShellRegions.Browser`: `ShowDuplicates()` remembers the browser (grid) and shows `DuplicatesViewModel`; `ShowBrowser()` puts the grid back. Picking any scope entry calls `ShowBrowser()`, and so does a library change (`LibraryShell.Update`). The browser keeps its scope, search and selection while the page is open. The smoke script step `nav <key>` picks an entry as a click does.

## The list

`GetDuplicateGroupsQuery(sort, filter)` returns `DuplicateGroupList` with the open groups from `IDuplicateGroupRepository.GetSummariesAsync` and the totals for the header (all open groups, whatever the filter). Sort: wasted space (default, largest first), type, group size. Filter: All, Exact, Similar; Similar is every type that is not Exact, so MFC-142's Visual groups appear there. Wasted space is the summed size of the members minus the largest one.

## The comparison

`GetDuplicateGroupQuery(groupId, keeperOrder)` returns `DuplicateGroupDetail`: the type, the members and the suggested keeper. A member has its name, format, size, resolution, date taken, folder, creation time and the user's metadata: the tags the user set, the descriptive fields the user edited and the favorite mark. Collections have no repository yet, so they are not counted. A member is not available (greyed, never the keeper) when it is Missing or Excluded, or its folder is offline.

`DuplicateComparisonViewModel` makes one card per member. Each card has the same attribute rows in the same order; a row whose value differs between members is tinted. The suggested keeper starts selected with a "Suggested" badge and the rule that picked it; the user can pick another available member. With more than four members the cards scroll sideways.

Keyboard (physical keys, D049): Up and Down change the group, Left and Right move between cards, Space sets the keeper, Z toggles zoom compare, Esc leaves it. Tab goes through the list, the buttons and the cards' radio buttons.

## Keeper rules

`KeeperSuggester.Suggest(members, order)` applies the rules in order; each keeps only the members that rank best by it, and the first rule that leaves one member decides. The rules: most user metadata, highest resolution, largest file, original format (a fixed ranking: raw, HEIC, JPEG, TIFF, lossless exports, then web formats such as WebP and AVIF) and oldest (file creation date). Unavailable members are never suggested; if every rule ties, the first member is taken.

The order is per library and lives in `settings.json` under `duplicates.keeperRules.<library ID>` as a JSON array of rule names (`KeeperRuleSettings`); the default order removes the key, and a saved order that lacks a rule gets the missing rules appended. A library moved to another machine uses the default order. The "Keeper rules" popover moves a rule up or down, resets to the default, and re-runs the suggestion.

## Zoom compare

Only groups that are not Exact have it (exact copies look the same). The attribute rows give way to large previews. All previews bind to one `ZoomState` (scale and centre as fractions of the image), so the mouse wheel, dragging and the double-click reset act on every member at the same area, whatever their resolutions. `ZoomImage` is the control: it draws the part of its image that the state names.

## Tests

`GetDuplicateGroupQueryTests` (real library), `DuplicateGroupListTests`, `KeeperSuggesterTests`, `DuplicatesViewModelTests`, `DuplicateComparisonViewModelTests`, `ZoomStateTests`, `ZoomImageTests` and `NavigationPanelTests`. The real-app script is `tools/ui-smoke/duplicates.smoke`.
