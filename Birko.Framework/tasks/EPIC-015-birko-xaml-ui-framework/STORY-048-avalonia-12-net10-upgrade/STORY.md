---
id: STORY-048
parent: EPIC-015
status: planned
created: 2026-07-29
---

# Avalonia 12 / .NET 10 upgrade for the Birko.Xaml stack

## User story

As a Birko.Xaml app developer, I want the XAML stack on Avalonia 12 and `net10.0`, so my desktop
app runs on the framework's own target runtime with a natively-built UI toolkit instead of resolving
an Avalonia `net8.0` asset from a maintenance branch.

## Why now (scouted 2026-07-29)

`Birko.Xaml.{Core,Avalonia,Shell}` sit at **Avalonia 11.2.3 on `net8.0`** — the .NET 8 island in a
repo whose every other project is `net10.0`. Current NuGet state:

| Line | Latest | TFMs shipped |
|---|---|---|
| Avalonia 12.x (current) | **12.1.0** (2026-07-09) | `net8.0`, **`net10.0`** |
| Avalonia 11.x (maintenance) | 11.3.18 (2026-06-23) | `net6.0`, `net8.0`, `netstandard2.0` |

Only 12.x ships a `net10.0` build (12 dropped `net6.0`/`netstandard2.0` and moved to SkiaSharp 3).
We are also 16 patches behind inside our own 11.x line.

## Scouting result — the upgrade is small, and it was measured, not estimated

The four projects were copied to a scratchpad, bumped to **12.1.0 / `net10.0` / xunit v3**, built and
run. Outcome: **144/144 tests green**, with exactly two hard code breaks. Full evidence is in the
`## Context` of each task below; the recipe is TASK-092.

- **`Kanban.cs` (4 errors)** — the clipboard/drag-drop overhaul: `DataObject` is now
  `[Obsolete(error)]`, `DragDrop.DoDragDrop` and `DragEventArgs.Data` are gone. Replaced with
  `DataTransfer` / `DataTransferItem` / `DoDragDropAsync` / `e.DataTransfer` and a typed
  `DataFormat<KanbanCard>`.
- **`FormFieldTypesTests.cs:123` (1 test failure)** — `GotFocus`/`LostFocus` now carry
  `FocusChangedEventArgs`; the test synthesized a bare `RoutedEventArgs` → `InvalidCastException`
  inside Avalonia's handler adapter.
- **Test project → xUnit v3** — `Avalonia.Headless.XUnit 12.1.0` depends on
  `xunit.v3.extensibility.core 3.2.2`, so v2 + v3 collide on `FactAttribute` (10× CS0433). The swap
  needed **zero source changes**.

**Everything else came through untouched** — all 20 AXAML files + 8 Shell views compile, including
the six generated theme dictionaries, `ThemeDictionaries`, `BThemeId` detection and
`ThemeCompositionTests`. Compiled bindings were already on, so 12's new default is a no-op for us,
and `TopLevel.GetTopLevel(…)` was already the 12-recommended form.

## Behaviour

- `Birko.Xaml.{Core,Avalonia,Shell}` target `net10.0` and reference Avalonia 12.1.x.
- Kanban drag-and-drop still moves a card between columns (typed `DataFormat<KanbanCard>` payload).
- The Avalonia suite runs green under xunit v3 **and** `dotnet test` still discovers it.
- Theme composition, runtime `ThemeVariant` swap and `BThemeId` detection behave exactly as on 11.
- Rendering is verified against a baseline, not just asserted structurally (TASK-095).

## The two real risks (neither is API surface)

1. **LiveCharts has no stable Avalonia-12 release** — TASK-093. Latest stable
   `LiveChartsCore.SkiaSharpView.Avalonia 2.0.5` pins Avalonia 11 + SkiaSharp 2.88; only
   `2.1.0-dev-798` targets Avalonia 12 + SkiaSharp 3. It restored and passed in the spike, but
   shipping a framework library on a `-dev` build is a decision, not a technicality. **This gates
   the bump.**
2. **The suite is visually blind** — TASK-095. Screenshots are `Save`d, never compared to a
   baseline (grepped: no baseline/golden/pixel comparison anywhere in the suite). So 144/144 green
   does **not** mean pixel-identical, and the Fluent-12 restyle + SkiaSharp 2.88→3 jump is exactly
   the kind of change that shifts rendering silently. Same class of gap as the CSS drift closed by
   `AxamlParityTests` — a gate should exist *before* the bump lands, so it has something to fail
   against.

## Tasks

| Task | What | Order |
|---|---|---|
| TASK-093 | LiveCharts Avalonia-12 story — resolve the blocker | first (gates 092) |
| TASK-095 | Screenshot baseline gate — build the visual net | first (gates 092) |
| TASK-092 | The bump itself + the two code fixes + xunit v3 | after 093 + 095 |
| TASK-094 | Obsolete-warning sweep (28 warnings) | after 092 |
| TASK-096 | Consumer-repo rollout in lockstep | after 092 |
