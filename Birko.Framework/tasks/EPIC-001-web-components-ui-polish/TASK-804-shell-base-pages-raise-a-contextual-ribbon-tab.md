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
- [ ] Raising the tab **opens it** (Symbio's ruling, 2026-11-02, from the prototype); an option keeps Office's
      "show, don't open" for other consumers. Default: open. Look: `contextual-style="band"`.
- [ ] Clearing it (deselect, or leaving the page) goes through `hideContextual()`, never by dropping the tab from
      `setTabs()`: dropped, the ribbon is left with no active tab and **collapses** (seen by the user in the prototype).
- [ ] `b-ribbon` remembers the previous normal tab also when the APP sets it. `_lastNormalTab` is written only in
      `_selectTab()` (a user click, `b-ribbon.ts:2802`), never when the `active` attribute changes, which is how
      `b-app-shell` sets it on every navigation. So after reaching a page by a link, `hideContextual()` falls back to the
      **first** tab. Measured in the prototype 2026-11-02: E-Shop › Orders, select, deselect → `active=services`, should
      be `commerce`. Fix: record it in `attributeChangedCallback` for `active` when the tab is not contextual.
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
