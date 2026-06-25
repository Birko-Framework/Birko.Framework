---
id: STORY-034
parent: EPIC-015
status: planned
created: 2026-06-25
---

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
