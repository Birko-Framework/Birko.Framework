---
id: TASK-970
parent: EPIC-001
feature: FEATURE-023
status: verify
priority: P2
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: a whole board can be locked, shown once

## Context

FEATURE-023 D3 (DraCode): DraCode locks every column of its board, and the lock shows in each header. One board setting now locks every column; the lock is shown once for the board and columns do not show their own. Built on `task/TASK-968` for v0.5.2.

## Acceptance criteria

- [x] `locked: true` in the board configuration refuses every move a person makes, by mouse and keyboard, with a board-level reason; the app's own `moveCard()` still works.
- [x] The board shows one lock notice; no column header shows its own lock.
- [x] Playground check.

## Evidence

Birko.Web `task/TASK-968` 3bcc1e1 (v0.5.2), Playground ef425d8. verify 0 failing (backport 492/492; carousel-smoke failed once, passed on the rerun); keyboard 244/244; a11y-name OK; contextual-ribbon 13/13; locale-keys 23/23; locale-switch 47/47; rerender-leak PASS; entry-points 433/433; device-fix 247/247; sortable 19/19; tree drag 17/17; escape-in-modal 27/27; theme-roundtrip 11/11; Components and Shell `tsc` clean. All from builds that printed "✓ Built".
- backport-smoke: a locked board shows one notice and no column lock; its cards are not draggable; Space on a card says "The board is locked: its cards cannot be moved."; the app's `moveCard()` still moves a card. Chrome: the gallery's lock board switch shows one Locked notice and no column locks.

## Human test plan

1. In the gallery's kanban, switch "lock board" on: one "Locked" notice above the board, no lock in the column headers.
2. Try to drag a card, and to move one with the keyboard (Space, then an arrow): refused, with the reason announced.
3. Switch it off: cards move again, and Done shows its own lock icon again.
