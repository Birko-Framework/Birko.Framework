# Birko.Xaml.Avalonia.Tests

## Overview

xUnit + FluentAssertions (Avalonia.Headless) test project for `Birko.Xaml.Avalonia` — the Avalonia
skin over `Birko.Xaml.Core`: the generated design-token dictionaries and theme system, restyled
Tier-1 controls, Tier-2 composites, the shell/ribbon/mobile views, and the schema-driven Form.

## Project Location

`C:\Source\Birko.Xaml.Avalonia.Tests\`

## Scope

- `ThemeSystemTests` — the generated `Tokens.axaml` loads; all four variants (Light/Dark/Neon/Finstat) resolve their color / length / radius / `var(...)` tokens; a runtime `RequestedThemeVariant` swap re-resolves `DynamicResource` brushes live.
- `ThemeManagerTests` — `AvaloniaThemeManager` lists the four themes, `SetTheme` switches the active variant and raises `ThemeChanged`, and unknown ids are ignored.
- `ControlThemeTests` — the first restyled controls (Button/TextBox + Card/Badge themes) consume tokens and re-skin per theme end to end (token → ControlTheme → live-swap).
- `Tier1ControlTests` — Tier-1 natives (ComboBox, ProgressBar, CheckBox, RadioButton, TabItem) are token-driven and re-theme per variant; named content themes (`BTag`/`BBadge`/`BCard`) are findable.
- `Tier1TailTests` — ToggleSwitch, BusySpinner, Breadcrumb (crumbs/separators/links/`ItemInvoked`), selected ListBoxItem, and the dropdown-menu (`MenuItem`/`MenuFlyoutPresenter`) themes.
- `DataGridTests` — the restyled Avalonia `DataGrid` column header uses the `BTableHeaderText` token foreground.
- `TrExtensionTests` — the `{l:Tr}` markup extension resolves through the Core `I18n` singleton and re-resolves live on locale change.
- `FormTests` / `DrawerTests` / `SplitPanelTests` — schema-driven `Form` (input per field, model binding, two-way write-back, required asterisk), the `Drawer` overlay, and the responsive `SplitPanel` collapse.
- `FormFieldTypesTests` — Form field-type parity: Switch, Markdown, Password, Radio, OptionGroup, defaults, Number clamp, Range slider (+ Birko slider template), Date/Time/DateTime/DateRange pickers, MultiSelect, Tags, File, and Hint.
- `ModalTests` — the `Modal` overlay toggles visibility with `IsOpen` (+ a screenshot capture).
- `FormModalTests` — the `FormModal` page-shape (Modal + Form + Save/Cancel): hosts the form, Save runs the command then closes, Cancel closes without saving.
- `CommandPaletteTests` — the `CommandPalette`: filter by search text, invoke-and-close, shows-all-when-empty.
- `ObjectTreeTests` / `XmlViewerTests` — the JSON/object tree and XML tree viewers render structure and fall back to a raw leaf on invalid input.
- `KanbanTests` — the `Kanban` board renders columns/cards and reflects observable-collection card moves.
- `MarkdownEditorTests` — the `MarkdownRenderer`/`MarkdownEditor`: headings/lists/code blocks, bold/italic inlines, and a live preview.
- `ChartTests` — the `BChart` over LiveCharts2 configures a series per model entry and maps `ChartKind` to Line/Column series.
- `NavigationTests` / `ViewLocatorTests` — `NavigationService` navigate/back/guard and the `ViewLocator` mapping of Split/List/Detail page VMs to the generic views.
- `ShellRenderTests` / `ShellChromeTests` — `ShellView`/`RibbonShellView` render the active page via the view locator, navigation swaps the page, palette commands come from modules + themes, tenant switcher visibility, and the Ctrl+K palette binding.
- `RibbonTests` — the `Ribbon`: renders tabs/groups/items, click runs the item action, tab switching, collapse (keeps the tab strip), and active-tab toggle-collapse.
- `MobileShellRenderTests` — `MobileShellView`: one bottom-nav item per module, renders the active page, and bottom-nav surface switching.
- `WakeLockTests` / `AudioCueTests` / `SyncStatusIndicatorTests` — the Avalonia device adapters (wake lock acquire/release idempotency, best-effort audio cue) and the `SyncStatusIndicator` content/class per `SyncStatus`.

Screenshot capture tests across the above render each surface via Skia and save a PNG (to `BIRKO_SHOTS`
or the temp dir) for visual parity per theme.

## Conventions

- Regular `Microsoft.NET.Sdk` csproj, `net8.0`, nullable + implicit usings enabled. Test framework is
  `Avalonia.Headless.XUnit` (with `[AvaloniaFact]` / `[AvaloniaTheory]`); `Avalonia.Skia` +
  `Avalonia.Themes.Fluent` are added so the parity tests capture real screenshots. References the
  `Birko.Xaml.Avalonia`, `Birko.Xaml.Core`, and `Birko.Xaml.Shell` `.csproj` assemblies (NOT `.projitems`).
- A single `TestApp` (`[assembly: AvaloniaTestApplication]`) mirrors a real Birko app — Fluent base
  theme + the generated `Tokens.axaml` and restyled ControlThemes via `BirkoTheme.axaml` — with Skia
  drawing enabled (`UseHeadlessDrawing = false`).
- Headless rendering; screenshot tests capture PNGs for visual parity per theme.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
