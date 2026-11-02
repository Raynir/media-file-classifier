# Design tokens

The UI takes every colour, size and font setting from design tokens (E13-S06). Tokens live in `src/MediaFileClassifier.Ui/Theming/`:

| File | Contents |
|------|----------|
| `Tokens.axaml` | Colours for the light and dark themes, spacing, radii, typography, opacity states and layout sizes. |
| `Icons.axaml` | Icon geometry, `Icon.*`. |
| `Styles.axaml` | Shared styles and classes built from the tokens. |

> **Provisional (M0).** The values were read by hand off the Figma mockups (file `u5giHVrftWw32xq3mjxXdu`), because the designer's token list (E13-S11-T03) isn't delivered yet. The redesign replaces the values and keeps the names, so views don't change. Figma has dark frames only, so the light values are derived. Status and provenance colours are placeholders.

## Using tokens

- Use `{DynamicResource Key}` for colours, so they follow theme switches. Sizes can use `{DynamicResource}` or `{StaticResource}`.
- Prefer a class from `Styles.axaml` over setting properties one by one: `<TextBlock Classes="Title" />`.
- Never write a literal colour, font size, margin, padding, spacing or corner radius in a view. A test fails if you do. `BorderThickness`, `Width` and `Height` may be literal when no token fits.
- Need a value that isn't there? Add a token rather than a literal, in both the `Dark` and `Light` dictionaries for colours. A test checks that both themes define the same colour keys.

## Token names

| Group | Keys | Notes |
|-------|------|-------|
| Surfaces | `Brush.Surface.Window`, `.Panel`, `.Content`, `.Tile`, `.Control`, `.Input`, `.Tooltip` | |
| Borders | `Brush.Border.Divider`, `.Control`, `.Selected`; `Border.Thin` | |
| Text and icons | `Brush.Text.Primary`, `.Secondary`, `.Tertiary`, `.Placeholder`; `Brush.Icon` | |
| States | `Brush.State.Hover`, `.Pressed`, `.Selected`, `.Focus`; `Opacity.Disabled`, `Opacity.Inactive` | |
| Item status | `Brush.Status.<Status>.Foreground` and `.Background`; `Icon.Status.<Status>` | `<Status>` is `Pending`, `Error`, `Missing`, `Offline` or `Excluded`. |
| Provenance | `Brush.Provenance.<Slot>.Foreground` and `.Background`; `Icon.Provenance.<Slot>` | `<Slot>` is `Ai`, `Edited`, `Accepted` or `NewSuggestion`. The look is designed in E07-S11-T01 (M3). |
| Spacing | `Space.XS` 4, `.S` 8, `.M` 12, `.L` 16, `.XL` 20, `.XXL` 24; `Padding.Panel`, `.Header`, `.Control`, `.Compact`, `.Strip`, `.Badge` | |
| Radii | `Radius.None`, `.Control`, `.Tile`, `.Tooltip`, `.Pill` | |
| Typography | `Font.Family`; `FontSize.Title`, `.Subtitle`, `.Body`, `.Caption`, `.Badge`; `FontWeight.Title`, `.Label`, `.Body` | Figma uses Roboto. The app bundles Inter until the font is decided. |
| Layout | `Size.Navigation.Expanded`, `.Collapsed`, `Size.DetailsPanel`, `.Collapsed`, `Size.Splitter`, `Size.Toolbar`, `Size.ContentHeader`, `Size.Control`, `Size.Control.Small`, `Size.Icon`, `.Small`, `.Large`, `Size.Tile.Width`, `.Height`, `Size.ListRow`, `Size.SettingControl` | The expanded panel widths are defaults: the user resizes the panels, and `ShellLayout` keeps their limits. `Size.DetailsPanel.Collapsed` isn't in Figma. |

## Classes in Styles.axaml

- **Text:** `Title`, `Subtitle`, `Label`, `Secondary`, `Tertiary`, `Caption`.
- **Surfaces:** `Border.Panel`, `Border.Content`.
- **Buttons:** `Button.Icon` for icon-only buttons. Give them `ToolTip.Tip` and `AutomationProperties.Name`.
- **Icons:** `PathIcon`, with `Small` and `Large` variants. Icons take the foreground of their container.
- **Item status:** `<Border Classes="StatusBadge Missing">`, with the label and an `Icon.Status.Missing` icon inside.
- **Provenance:** `<Border Classes="Provenance Ai">`.

## Icons

The icons are Material Symbols Outlined, weight 400, the set the Figma mockups use. They're licensed under Apache License 2.0, Copyright Google LLC. The path data comes from the `@material-symbols/svg-400` 0.40.2 package. Use an icon as `<PathIcon Data="{StaticResource Icon.FolderOpen}" />`. To add one, copy the `d` attribute of `<name>.svg` from that package into `Icons.axaml`, with the file name in a comment. Choosing the final icon set is a design sub-task (E13-S06-T03).

## Themes

The app follows the Windows light or dark setting. `ThemeService.Mode` overrides it, and so does the `--theme=light` or `--theme=dark` command-line switch. Saving the choice comes with the settings screen (E17).
