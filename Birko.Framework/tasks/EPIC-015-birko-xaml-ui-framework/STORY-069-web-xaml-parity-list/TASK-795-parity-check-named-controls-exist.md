---
id: TASK-795
parent: STORY-069
feature: FEATURE-021
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-30
depends-on: [TASK-793]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Parity check: a named desktop counterpart must exist

## Context

FEATURE-021 D8. The list must not claim a port that is not there. For rows whose status is `built`, `themed` or
`form-only`, the desktop-counterpart column must resolve: a type (e.g. `Birko.Xaml.Avalonia.Controls.TagInput`,
`Birko.Xaml.Core.Mvvm.ListPageViewModel`) found by reflection in the Birko.Xaml assemblies; a themed Avalonia control
(e.g. `TabControl`) for which the Birko theme has a `ControlTheme`; a named theme key (e.g. `BCard`) present in the
theme's resources; a `FieldType` member for `form-only`. Settle one notation for the column so the test can tell these
apart, and apply it to every row (both sections if TASK-794 is in by then; otherwise TASK-794 adopts it).
`DataGridStyles.axaml` is Styles, not a resource dictionary — the `DataGrid` row needs a rule of its own.

## Acceptance criteria

- [x] One notation for the counterpart column, documented at the top of the list.
- [x] The test fails, naming the row, when a `built` / `themed` / `form-only` counterpart cannot be resolved; a fixture test per kind.
- [x] Proved it can fail: renaming one counterpart in the list to a non-existent type fails the test.

## Out of scope

- Checking that the desktop control behaves like the web one — the list records where, not how well.

## Human test plan

N/A — fully covered by automated tests (the check is the deliverable; its fixture and proved-to-fail runs are the proof).

## Implementation plan

1. Notation: every name in backticks in the Desktop column is a counterpart and is checked — a type (`TagInput`), a
   member (`DialogService.Notify`), or a Birko theme key (`BCard`). Free words around them are not. Rules by status:
   `built` → a type or member in the Birko.Xaml assemblies; `form-only` → `FieldType.<member>`; `themed` → a Birko
   theme key, or an Avalonia control the Birko theme styles (a type-keyed `ControlTheme` in `BirkoTheme.axaml`, or a
   `Style` in `DataGridStyles.axaml` — the DataGrid rule); `needs-theme` → an Avalonia control Birko does NOT style
   (so a row cannot stay `needs-theme` after its theme lands). `built`/`themed`/`form-only` must name at least one.
2. Fix rows the notation cannot read: `BBadge`/`BTag`/`BCard` alone (the `ContentControl` goes to the Note);
   `b-dropdown-menu` names `MenuFlyoutPresenter` and `MenuItem` (Birko themes those, not `MenuFlyout`);
   `b-color-picker`'s `ColorPicker` moves to the Note (its package is not referenced, so it cannot be resolved).
3. `Parity/Counterparts.cs`: the resolver (Birko assemblies, Avalonia control assemblies, the Birko theme dictionary
   loaded on its own so Fluent's themes do not count, the DataGrid styles). `[AvaloniaFact]`s: one fixture per kind
   and status, and the live check over both sections — which needs no web checkout, so it always runs.

## Review log

- 2026-10-30 — Built; **done** (Human test plan N/A). Notation and rules at the head of `PARITY.md`. Rows changed to
  fit it: `BBadge` / `BTag` / `BCard` alone (the `ContentControl` moved to the Note); `b-dropdown-menu` names
  `MenuFlyoutPresenter` and `MenuItem`, which the Birko theme styles (not `MenuFlyout`, which it does not);
  `b-color-picker`'s `ColorPicker` moved to the Note, since its package is not referenced and cannot be resolved.
- `Parity/Counterparts.cs`; `ParityCounterpartTests` 23 (a live case per section, 9 holding fixtures, 9 failing
  fixtures, 3 "names no counterpart"). The live check needs no web checkout, so it runs in `build-and-test.yml`.
  What it confirmed about the list: every counterpart exists, and `SelectableTextBlock`, `Carousel` and
  `NumericUpDown` really have no Birko theme yet (`needs-theme` holds).
- Bug found while building: a nested type of a generic class reports `IsGenericType` without a backtick in its name;
  the name strip now keys on the backtick.
- **Proved it can fail** on the real list: `TagInput` → `TagInputX` failed with "line 93: b-tag-input (built) —
  TagInputX is not a type or member in the Birko.Xaml assemblies"; `b-qty-stepper` naming `Button` failed with
  `… is already styled by the Birko theme — the row is themed`. Restored from a copy, green.
- Process slip, recovered: the first restore used `git checkout -- PARITY.md`, which also discarded this task's
  uncommitted notation edits; they were re-applied and re-verified before close.
- Gate (inline passes): conventions, intent (all three criteria), correctness — no blocker; security n/a (test code);
  comments — the one inline comment says why the theme is loaded on its own, which nothing else records.
