---
id: TASK-992
parent: EPIC-001
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: [TASK-978, TASK-989, TASK-545]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A split page's detail panel comes after the table's pager in Tab order

## Context

Seen by the Symbio session at TASK-989's keyboard test (Symbio HR → Positions, Birko.Web v0.5.5, 2026-11-25): opening a
row with Enter (TASK-978) shows the detail panel beside the table, but the panel's buttons (close, Edit, Delete) come
after everything in the master panel in Tab order: the table's row stop, the page-size select and the pager. Reaching
Delete from the opened row took 5 Tabs. `BaseSplitPage.renderContent` slots the master card before the detail card in
`b-split-panel`, so DOM order puts the whole master, pager included, first. Not wrong, but a keyboard user who just opened
a row has to Tab past controls they did not come for, and the panel they opened is the one they want next.

## Acceptance criteria

- [ ] Decide with the user, from the options below (or another), how a keyboard user reaches the detail panel they just
      opened; record the decision and why the others lost.
- [ ] Built in `BaseSplitPage` (so every split page gets it), with pointer behaviour unchanged.
- [ ] Focus never falls to `<body>` when the detail panel opens, closes or changes record; closing it returns focus to the
      row it came from.
- [ ] Playground check (keyboard only, a split-page fixture): open a row with Enter, reach Delete, close the panel, focus
      back on the row; red before the change.
- [ ] README (Shell, split page) and CHANGELOG.

## Options to decide between

- **A. Enter moves focus into the panel** (its heading or first action); Escape / close returns to the row. One keystroke
  from row to panel; focus moving on Enter is what a dialog does, and it may surprise someone who meant to keep browsing
  rows with the arrows.
- **B. The detail panel comes right after the table's row stop in Tab order** (before the page-size select and pager),
  focus staying on the row. Enter keeps its "open" meaning, one Tab reaches the panel; reordering Tab without moving the
  DOM needs care (a roving stop or `tabindex` order) and must not break the pager's own order.
- **C. A pane shortcut (F6, as Windows and Office move between panes)** plus A or B. Expected by power users, invisible
  to everyone else without a hint.

## Out of scope

- The table's own keyboard model (TASK-978) and focus after Undo (TASK-989), both done.

## Human test plan

- [ ] On Symbio HR → Positions, keyboard only: open a position with Enter, reach Delete in the fewest keys the chosen
      option promises, close the panel, focus is back on the row.
