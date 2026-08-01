---
id: TASK-092
parent: STORY-048
feature: FEATURE-015
status: todo
priority: P2
assignee: ai
created: 2026-07-29
depends-on: [TASK-093, TASK-095]
blocks: [TASK-094, TASK-096]
pr: null
github-issue: null
jira-key: null
---

# Bump Birko.Xaml to Avalonia 12.1.0 / `net10.0` + xunit v3 (Kanban DataTransfer, focus event)

## Context

The whole migration, **verified end-to-end in a scratchpad on 2026-07-29** — four repos copied,
bumped, built, suite run: **144/144 green** in 6.5s. This task is applying that exact recipe to the
real repos. Do not re-derive it; the diffs below are what actually compiled and passed.

Blocked on **TASK-093** (LiveCharts has no stable Avalonia-12 release — that decision determines
which `LiveChartsCore.SkiaSharpView.Avalonia` version this task pins) and on **TASK-095** (the
screenshot baseline must exist *before* the bump, or the rendering shift lands unobserved).

Four repos move together (polyrepo — each is its own git repo):
`Framework/Birko.Xaml.Core`, `Framework/Birko.Xaml.Avalonia`, `Framework/Birko.Xaml.Shell`,
`Framework.Tests/Birko.Xaml.Avalonia.Tests`.

### 1. Project files

All four: `<TargetFramework>net8.0</TargetFramework>` → `net10.0`.

`Birko.Xaml.Avalonia.csproj` — `Avalonia`, `Avalonia.Themes.Fluent`, `Avalonia.Controls.DataGrid`
`11.2.3` → `12.1.0`; LiveCharts per TASK-093.
`Birko.Xaml.Shell.csproj` — `Avalonia` `11.2.3` → `12.1.0`.
`Birko.Xaml.Core.csproj` — TFM only (Avalonia-free, EPIC constraint #1 — keep it that way).
`Birko.Xaml.Avalonia.Tests.csproj` — `Avalonia.Headless.XUnit`, `Avalonia.Skia`,
`Avalonia.Themes.Fluent` → `12.1.0`; `xunit 2.9.3` → `xunit.v3 3.2.2`; **keep
`xunit.runner.visualstudio 3.1.5`** (see gotcha below).

### 2. `Kanban.cs` — the only production code break (4 × CS0433/CS0619/CS0117/CS1061)

Avalonia 12 removed `DataObject`, `DragDrop.DoDragDrop`, `DragEventArgs.Data`. Verified replacement:

```csharp
// line 23 — was: private const string CardFormat = "birko-kanban-card";
private static readonly DataFormat<KanbanCard> CardFormat =
    DataFormat.CreateInProcessFormat<KanbanCard>("birko-kanban-card");

// lines 110-112
var data = new DataTransfer();
data.Add(DataTransferItem.Create(CardFormat, card));
await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);

// line 119 (OnDrop)
if (e.DataTransfer?.TryGetValue(CardFormat) is not KanbanCard card || Columns is null) return;
```

Typed `DataFormat<T>` is a net win — the old `Get()` returned `object` and the cast was unchecked.

### 3. `FormFieldTypesTests.cs:123` — the only test break

`GotFocus`/`LostFocus` now carry `FocusChangedEventArgs`; a synthesized bare `RoutedEventArgs`
throws `InvalidCastException` inside `Interactive.InvokeAdapter`:

```csharp
box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
```

The other 7 `RaiseEvent` sites in the suite are `Button.ClickEvent` / `KeyEventArgs` — unaffected.

### 4. xUnit v3 — package-only

`Avalonia.Headless.XUnit 12.1.0` depends on `xunit.v3.extensibility.core 3.2.2`, so v2 and v3 both
define `FactAttribute` (10 × CS0433). Swapping to `xunit.v3` needed **zero source changes**:
`[AvaloniaFact]`/`[AvaloniaTheory]`, `[InlineData]`, `TestApp.cs`'s
`[assembly: AvaloniaTestApplication]` and FluentAssertions 7.0.0 all carried over unchanged.

**Gotcha:** xunit v3 runs on Microsoft.Testing.Platform (the assembly becomes an exe). Drop
`xunit.runner.visualstudio` and `dotnet test` reports *"No test is available"* while the exe still
passes 144/144 — a silently green CI that runs nothing. Keeping `xunit.runner.visualstudio 3.1.5`
alongside `xunit.v3` restores normal `dotnet test` discovery (verified: 144 passed either way).

### What needs no work (checked, not assumed)

All 20 `Birko.Xaml.Avalonia` AXAML files and 8 Shell views compile clean, **including the six
generated theme dictionaries**, `ThemeDictionaries`, `BThemeId`, `AvaloniaThemeManager`,
`ThemeCompositionTests` and `ControlThemeTests`. `AvaloniaUseCompiledBindingsByDefault` was already
`true`, so 12's new default changes nothing. `TopLevel.GetTopLevel(…)` (`Form.cs:336`,
`MobileShellView.axaml.cs:23`) is already the 12-recommended form.

## Acceptance criteria

- [ ] All four projects target `net10.0` and reference Avalonia 12.1.x; solution + `.code-workspace` registrations still resolve.
- [ ] `Kanban.cs` uses `DataTransfer`/`DataTransferItem`/`DoDragDropAsync`/`e.DataTransfer` with a typed `DataFormat<KanbanCard>`; drag-a-card-between-columns still works.
- [ ] `FormFieldTypesTests` raises `FocusChangedEventArgs`; the number-clamp-on-commit test passes.
- [ ] Test project on `xunit.v3 3.2.2` **with** `xunit.runner.visualstudio` retained — `dotnet test` discovers and runs the suite (not just the exe).
- [ ] `Birko.Xaml.Avalonia.Tests` 144/144 green; `Birko.Xaml.Core.Tests` (41) and `Birko.DesignTokens.Tests` (42) still green.
- [ ] `Birko.Xaml.Core` still has zero Avalonia references (EPIC-015 WPF-addendum constraint #1).
- [ ] TASK-095's baseline gate passes, or every intentional pixel diff is reviewed and re-baselined with a note saying why.
- [ ] `Recent Updates` entry in `CLAUDE.md` + each affected project's `CLAUDE.md`; `verify-birko-conventions` clean.

## Out of scope

- The 28 obsolete warnings (`Watermark`, `Bitmap.Save`) — TASK-094. They do not block the build.
- Building the baseline gate — TASK-095.
- The LiveCharts version decision — TASK-093.
- Consumer repos — TASK-096.
- WPF (still deferred per EPIC-015).

## Human test plan

- [ ] Launch the gallery app and drag a Kanban card between two columns — it moves, and the drop lands in the target column (the rewritten drag-drop path is the one thing headless tests exercise least).
- [ ] Swap all four themes at runtime in the gallery — light/dark/neon/finstat each re-resolve; no missing-resource fallbacks or unstyled controls.
- [ ] Confirm text renders correctly in a real windowed app. `TestApp.BuildAvaloniaApp()` calls `.UseSkia()` **without** the `.UseHarfBuzz()` that the Avalonia 12 docs now say is required for text shaping; it rendered fine headless, but a windowed app is the honest check. If text is broken, add the `Avalonia.HarfBuzz` package + `.UseHarfBuzz()`.
- [ ] Eyeball a form with placeholders, a DataGrid, and a modal against an 11.x screenshot — Fluent 12 restyled its own control themes, so a visual shift is expected somewhere.

## Implementation plan

_Populated by `/tasks plan TASK-092` — leave empty until then._
