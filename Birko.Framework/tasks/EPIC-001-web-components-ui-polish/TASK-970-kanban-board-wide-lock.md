---
id: TASK-970
parent: EPIC-001
feature: FEATURE-023
status: in-progress
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

- [ ] `locked: true` in the board configuration refuses every move a person makes, by mouse and keyboard, with a board-level reason; the app's own `moveCard()` still works.
- [ ] The board shows one lock notice; no column header shows its own lock.
- [ ] Playground check.

## Human test plan

1. In the gallery's kanban, switch "lock board" on: one "Locked" notice above the board, no lock in the column headers.
2. Try to drag a card, and to move one with the keyboard (Space, then an arrow): refused, with the reason announced.
3. Switch it off: cards move again, and Done shows its own lock icon again.
