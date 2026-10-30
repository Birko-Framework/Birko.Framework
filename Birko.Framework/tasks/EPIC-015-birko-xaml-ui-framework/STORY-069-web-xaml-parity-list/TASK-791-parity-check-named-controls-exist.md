---
id: TASK-791
parent: STORY-069
feature: FEATURE-021
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-30
depends-on: [TASK-789]
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
apart, and apply it to every row (both sections if TASK-790 is in by then; otherwise TASK-790 adopts it).
`DataGridStyles.axaml` is Styles, not a resource dictionary — the `DataGrid` row needs a rule of its own.

## Acceptance criteria

- [ ] One notation for the counterpart column, documented at the top of the list.
- [ ] The test fails, naming the row, when a `built` / `themed` / `form-only` counterpart cannot be resolved; a fixture test per kind.
- [ ] Proved it can fail: renaming one counterpart in the list to a non-existent type fails the test.

## Out of scope

- Checking that the desktop control behaves like the web one — the list records where, not how well.

## Human test plan

N/A — fully covered by automated tests (the check is the deliverable; its fixture and proved-to-fail runs are the proof).

## Implementation plan

## Review log
