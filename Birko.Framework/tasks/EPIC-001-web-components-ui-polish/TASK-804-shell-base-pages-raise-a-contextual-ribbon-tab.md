---
id: TASK-804
parent: EPIC-001
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-02
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Shell base pages can raise a contextual ribbon tab for the selected record

Asked for by Symbio (TASK-1028), to adopt `b-ribbon`'s contextual tabs (FEATURE-019 TASK-734) in the Shell.

## Context

`b-app-shell` still forwards only the old `ribbon-actions` event to `b-ribbon.setContextActions()` — a strip of
loose buttons, which no Symbio page uses. `b-ribbon` now has contextual tabs (`RibbonTab.contextualSet`,
`setContextualSets()`, `showContextual()`, `hideContextual()`), but nothing in the Shell can reach them: the tabs are
rebuilt by `refreshRibbon()` from `getRibbonTabs()`, so a page cannot add one without being overwritten on the next
module or tenant change.

Typical consumer: a `BaseSplitPage` / `BaseDetailPage` where a record (an order, an invoice) is selected, whose
workflow actions (Confirm, Ship, Cancel, Print) belong in a tab named after the record type, shown while it is
selected.

## Acceptance criteria

- [ ] A page declares a contextual tab with a hook (e.g. `getContextualTab(record): RibbonTab | null` on
      `BaseSplitPage` and `BaseDetailPage`), raised on selection and cleared on deselection and on leaving the page.
- [ ] `b-app-shell` merges the page's contextual tab into what `refreshRibbon()` sets, so a module, tenant or
      locale refresh keeps it; leaving the page (route change) removes it.
- [ ] Clicking a contextual command calls the page (a hook such as `onContextualAction(itemId, record)`), not the
      shell's `onItemClick`; disabled state follows the record (re-raised on record change).
- [ ] The tab is selected when raised only if the user had not picked another tab (Office behaviour: shown, not
      forced), with an option to select it.
- [ ] `setContextActions` / `ribbon-actions` stays working (deprecated in the README), so no consumer breaks.
- [ ] Playground smoke: select a record → tab shown with its commands; deselect → gone; a module switch with a
      record selected keeps it; a command reaches the page hook. Shell README, CHANGELOG, version bump.

## Out of scope

- The split-button category ribbon — TASK-803.
- Which Symbio pages get one — Symbio TASK-1028.

## Human test plan

- [ ] Playground split page: select a row, then switch ribbon tabs and back. Expected: the contextual tab stays,
      in its band colour, until the row is deselected or the page left.

## Implementation plan

_Populated by `/tasks plan TASK-804` — leave empty until then._
