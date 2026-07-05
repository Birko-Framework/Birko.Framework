---
id: STORY-034
parent: EPIC-015
status: done
created: 2026-06-25
closed: 2026-07-05
---

## Done (2026-07-05) — Tier-1 complete

All ~20 Tier-1 controls delivered, token-driven, tested headlessly, shown in the gallery across
all four themes. Final additions: **Modal** (centered dialog over a backdrop; a `Form` inside it =
the FormModal pattern, screenshot-verified) and **DataGrid** (`data-table`) restyled with Birko
tokens — the signature header band via `--b-table-header-*` (finstat's dark charcoal band verified),
shipped as `Controls/DataGridStyles.axaml` (Styles + the `Avalonia.Controls.DataGrid` package;
added to `Application.Styles`).

## Progress (2026-07-04)

Tier-1 sweep underway in `Birko.Xaml.Avalonia/Controls/` (split into category files: Buttons /
Inputs / Toggles / Surfaces / Indicators / Overlays, merged by `Controls.axaml`). All token-driven
via `{DynamicResource B*}`; verified across all four themes in the gallery + screenshots. **23
headless tests green.**

**Done (16):** Button, TextBox (single + multiline), Card, Badge, Tag, TabControl/TabItem,
CheckBox, RadioButton, ProgressBar, ToolTip, ComboBox (+ ComboBoxItem), **ToggleSwitch**,
**BusySpinner** (rotating Arc), **dropdown menu** (`MenuFlyoutPresenter` + `MenuItem`), **Breadcrumb**.

**Remaining (the two heavy ones):**
- `table` / `data-table` — Avalonia `DataGrid` is a separate package + row-height parity (last Tier-1 item).
- `modal` overlay — overlaps STORY-035/036 (like `Drawer`, an overlay control).

**Gotchas found:** `ToggleSwitch` needs `PART_MovingKnobs`/`PART_SwitchKnob` (and `PART_SwitchKnob`
must be a `Panel`); a top-level ControlTheme animation `Style` silently breaks theme application
(scope animations inside the element's own `.Styles`); `Spinner` collides with Avalonia's built-in
(named `BusySpinner`).

**Gotcha fixed:** a `double` radius token can't bind to `CornerRadius`; the STORY-029 generator now
emits `--b-radius*` as `CornerRadius` resources (CSS byte-parity unaffected).

# Tier 1 — restyled native controls (~20)

## User story

As an app developer, I want the common controls restyled to the Birko look via token-based
ControlThemes, so ~80% of the visual surface matches Birko.Web with mostly mechanical work.

## Behaviour

- `ControlTheme`s over native Avalonia controls for: input, select, button, checkbox, switch, radio, textarea, tabs, card, badge, tag, tooltip, table, data-table, modal, dropdown, progress, spinner, breadcrumb (≈20). (`Form`/`Drawer`/`SplitPanel` are split out into STORY-033.)
- Every control is styled **exclusively** through `--b-*`-derived token resources — no hard-coded colors/sizes (mirrors the Birko.Web rule).
- API is binding-first MVVM: `StyledProperty`/`DirectProperty` + bindings, not imperative `setConfig()`. Visual/token parity carries over 100%; the programming model is idiomatic Avalonia.
- `size` convention carried over where it maps (vertical-footprint via `--b-control-min-height*`, text-scale, etc.).
- Each control appears in the gallery app across all four themes.
- **Edge cases:** `data-table` (Avalonia DataGrid is a separate package); uniform row height parity with `b-table`'s `--b-table-row-height`.
