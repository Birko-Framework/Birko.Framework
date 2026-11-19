---
id: TASK-940
parent: EPIC-001
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-11-19
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A `b-data-table` row with no applicable row action shows no `⋮`

Reported by the user, 2026-11-19, seen in Symbio's Customers → Erasure requests. `b-data-table` drew the row-action
trigger on every row whenever `rowActions` was non-empty (the `__actions` column's `render`), but filtered the actions
by `visible(row)` only when the menu opened. A row where no action applies (a completed or rejected erasure request)
got a `⋮` button that opened an empty menu.

## Change (Birko.Web v0.4.19)

- The `__actions` column's `render` returns an empty cell when no `rowActions` entry passes
  `!a.visible || a.visible(row)` — the same predicate the menu uses when it opens.
- The column itself stays (the user's spec), so cell alignment is unchanged.
- Docs: API.md (`RowAction` shape and a Row actions paragraph), CHANGELOG.

## Evidence

Playground backport-smoke, three TASK-940 checks (a row with an applicable action keeps its trigger; a row with none
has no trigger; both rows keep the same cell count):
- On Birko.Web main 1 of 3 fails: the row with no applicable action has 1 trigger.
- With the change 372/372, 3 of 3 runs; verify.mjs reports 0 failing checks.

keyboard-check 225/225, contextual-ribbon-check 13/13, rerender-leak-check PASS, a11y-name-check OK (43), Components
`tsc` clean.

In Symbio (checked by the Symbio session, 2026-11-19): Symbio.UI built with `BIRKO_SRC` set to the TASK-940 worktree,
Customers → Erasure requests on the Testing API, in Chrome. The pending request shows `⋮` and its menu offers both
actions; the 6 completed or rejected rows show an empty cell, and the columns stay aligned. Symbio's service worker
kept serving the previous bundle after the rebuild: unregister it and clear its caches before re-checking. The
"token drift" build warning appears on Web main too, so it isn't from this change.

## Acceptance criteria

- [x] A row where no row action is visible renders no trigger, and its actions cell is still there.
- [x] A row with at least one visible action keeps its trigger and its menu.

## Human test plan

1. In Symbio, open Customers → Erasure requests with at least one pending and one completed or rejected request.
2. A pending request shows `⋮`, and its menu lists its actions.
3. A completed or rejected request shows no `⋮`; its row's cells line up with the others.
