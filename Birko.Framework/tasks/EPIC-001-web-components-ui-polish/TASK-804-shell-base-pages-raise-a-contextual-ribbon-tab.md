---
id: TASK-804
parent: EPIC-001
feature: null
status: done
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

- [x] A page declares a contextual tab with a hook (e.g. `getContextualTab(record): RibbonTab | null` on
      `BaseSplitPage` and `BaseDetailPage`), raised on selection and cleared on deselection and on leaving the page.
- [x] `b-app-shell` merges the page's contextual tab into what `refreshRibbon()` sets, so a module, tenant or
      locale refresh keeps it; leaving the page (route change) removes it.
- [x] Clicking a contextual command calls the page (a hook such as `onContextualAction(itemId, record)`), not the
      shell's `onItemClick`; disabled state follows the record (re-raised on record change).
- [x] Raising the tab **opens it** (Symbio's ruling, 2026-11-02, from the prototype); an option keeps Office's
      "show, don't open" for other consumers. Default: open. Look: `contextual-style="band"`.
- [x] Clearing it (deselect, or leaving the page) goes through `hideContextual()`, never by dropping the tab from
      `setTabs()`: dropped, the ribbon is left with no active tab and **collapses** (seen by the user in the prototype).
- [x] `b-ribbon` remembers the previous normal tab also when the APP sets it. `_lastNormalTab` is written only in
      `_selectTab()` (a user click, `b-ribbon.ts:2802`), never when the `active` attribute changes, which is how
      `b-app-shell` sets it on every navigation. So after reaching a page by a link, `hideContextual()` falls back to the
      **first** tab. Measured in the prototype 2026-11-02: E-Shop › Orders, select, deselect → `active=services`, should
      be `commerce`. Fix: record it in `attributeChangedCallback` for `active` when the tab is not contextual.
- [x] `setContextActions` / `ribbon-actions` stays working (deprecated in the README), so no consumer breaks.
- [x] Playground smoke: select a record → tab shown with its commands; deselect → gone; a module switch with a
      record selected keeps it; a command reaches the page hook. Shell README, CHANGELOG, version bump.

## Out of scope

- The split-button category ribbon — TASK-803.
- Which Symbio pages get one — Symbio TASK-1028.

## Human test plan

- [x] Playground split page: select a row, then switch ribbon tabs and back. Expected: the contextual tab stays,
      in its band colour, until the row is deselected or the page left. **PASS (the user, 2026-11-03)**, in the
      "Contextual tab (Shell)" card on the TASK-804 build.

## Implementation plan

- `Birko.Web.Shell/src/pages/contextual-ribbon.ts`: `ContextualRibbon` (a `RibbonContextualSet`, its tabs, `open?`,
  default true) and `ContextualRibbonLink`, which a page owns. A page raises by a bubbling `ribbon-contextual` event,
  and the shell answers synchronously with a handle (`update` / `clear`) put on the event's detail. The page keeps the
  handle because it must clear while leaving the DOM, when an event from it reaches nobody (`onUnmount` runs after
  disconnect). A new record key opens it; a re-raise for the same record keeps whatever tab is open.
- `BaseSplitPage` / `BaseDetailPage`: `getContextualRibbon(entity)`, `onContextualAction(itemId, entity)`,
  `refreshContextualRibbon()`. Raised after each detail load (split: selection and `reloadDetail()`; detail: load and
  save), cleared on `deselectEntity()` and in `disconnectedCallback` (not `onUnmount`, which a subclass may override
  without super).
- `BAppShell`: the tab is shell state (`_ctx`, `_ctxOpenTab`), merged into every `refreshRibbon()` with
  `setContextualSets`, and `active` (render and refresh) names the open contextual tab, since `getActiveTabId()` names
  the module. `tab-change` for a contextual tab and `item-click` on its commands do not reach `onTabChange` /
  `onItemClick`. Clearing hides the set first (quietly: its `tab-change` back is no navigation), then drops the tabs.
  A page gone without clearing loses it on the next refresh. One page's tab at a time.
- `b-ribbon`: `attributeChangedCallback` records a non-contextual `active` as the last normal tab.
- Playground: `contextual-ribbon-smoke` (a real BAppShell, split and detail pages), a "Contextual tab (Shell)" card,
  `contextual-ribbon-check.mjs` (real mouse). v0.4.3.

## Built (2026-11-03), on branches, not merged

Birko.Web `task/TASK-804` c68c678 (worktree `Web.TASK-804`, v0.4.3); Playground `task/TASK-804` b3b62da (worktree
`Birko.Web.Playground.TASK-804`). `contextual-ribbon-smoke` 20/20, `contextual-ribbon-check.mjs` 7/7 with a real
mouse. Twelve mutations, each caught: a contextual tab-change reaching `onTabChange`, the hide not quiet, the refresh
dropping the tabs, the refresh resetting `active`, a command going to the shell, no clear on disconnect, deselect
keeping it, never opening, `open: false` ignored, the detail page never raising, the ribbon forgetting the app-set
tab, the tabs dropped without hiding the set (seen as `isContextualShown` still true; in this shell the ribbon does not
collapse, since `refreshRibbon` sets `active` after `setTabs`). Shell and Components `tsc` clean, the new Playground
files type-check against the branch. verify.mjs 0 failing, keyboard-check 222/222, ribbon-a11y-check 22/22,
category-ribbon-check 19/19, entry-points-check 427/427, locale-switch 47/47; subpath, rerender-leak, device-fix,
a11y-name and testing-smoke checks pass.

## Merged (2026-11-03), not pushed

Birko.Web main afc9edb, tagged **v0.4.3** locally; Playground main 616c965. Not pushed, at the user's word (batch the
pushes to save CI runs): push both, with the tag, with the next batch. Symbio told before the merge; its dev server
watches the local checkout, so it already builds on v0.4.3. Worktrees and branches removed.
