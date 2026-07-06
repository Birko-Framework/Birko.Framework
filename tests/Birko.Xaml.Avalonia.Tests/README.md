# Birko.Xaml.Avalonia.Tests

xUnit + FluentAssertions (Avalonia headless) tests for [`Birko.Xaml.Avalonia`](../Birko.Xaml.Avalonia).

## Coverage

- **`ThemeSystemTests`** / **`ThemeManagerTests`** — generated `Tokens.axaml` loads, all four variants resolve their tokens, live `DynamicResource` re-resolution on variant swap, and the `AvaloniaThemeManager` (list / `SetTheme` / `ThemeChanged`).
- **`ControlThemeTests`** — Button/TextBox + Card/Badge themes consume tokens and re-skin per theme.
- **`Tier1ControlTests`** / **`Tier1TailTests`** — Tier-1 natives (ComboBox, ProgressBar, CheckBox, RadioButton, TabItem, ToggleSwitch, BusySpinner, Breadcrumb, ListBox, dropdown menus) are token-driven and re-theme per variant.
- **`DataGridTests`** — the restyled `DataGrid` column header uses the token foreground.
- **`TrExtensionTests`** — the `{l:Tr}` markup extension resolves via the Core `I18n` singleton and re-resolves on locale change.
- **`FormTests`** / **`DrawerTests`** / **`SplitPanelTests`** — schema-driven `Form`, `Drawer` overlay, and responsive `SplitPanel`.
- **`FormFieldTypesTests`** — every Form `FieldType` (Switch, Markdown, Password, Radio, OptionGroup, Number, Range, Date/Time/DateTime/DateRange, MultiSelect, Tags, File) plus defaults, clamping, and hints.
- **`ModalTests`** / **`FormModalTests`** — the `Modal` overlay and the `FormModal` page-shape (Save runs then closes, Cancel closes without saving).
- **`CommandPaletteTests`** — the command palette filter / invoke-and-close.
- **`ObjectTreeTests`** / **`XmlViewerTests`** — the JSON/object and XML tree viewers, with raw-leaf fallback on invalid input.
- **`KanbanTests`** — the kanban board columns/cards and reactive card moves.
- **`MarkdownEditorTests`** — the markdown renderer/editor (blocks, inlines, live preview).
- **`ChartTests`** — the `BChart` over LiveCharts2 (series per entry, Line/Column kinds).
- **`NavigationTests`** / **`ViewLocatorTests`** — `NavigationService` and the page-VM → view mapping.
- **`ShellRenderTests`** / **`ShellChromeTests`** / **`RibbonTests`** / **`MobileShellRenderTests`** — the shell, ribbon, and mobile-shell views, chrome (palette, tenants, Ctrl+K), and bottom-nav.
- **`WakeLockTests`** / **`AudioCueTests`** / **`SyncStatusIndicatorTests`** — the Avalonia device adapters and the sync-status chip.

Many tests also capture Skia screenshots (PNGs to `BIRKO_SHOTS` or the temp dir) for per-theme visual parity.

## Test framework

- xUnit
- FluentAssertions
- Avalonia.Headless.XUnit (+ Skia for screenshot capture)

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
