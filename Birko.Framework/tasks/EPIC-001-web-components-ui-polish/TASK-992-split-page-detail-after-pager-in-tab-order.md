---
id: TASK-992
parent: EPIC-001
feature: null
status: verify
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

- [x] Decide with the user, from the options below (or another), how a keyboard user reaches the detail panel they just
      opened; record the decision and why the others lost.
- [x] Built in `BaseSplitPage` (so every split page gets it), with pointer behaviour unchanged.
- [x] Focus never falls to `<body>` when the detail panel opens, closes or changes record; closing it returns focus to the
      row it came from.
- [x] Playground check (keyboard only, a split-page fixture): open a row with Enter, reach Delete, close the panel, focus
      back on the row; red before the change.
- [x] README (Shell, split page) and CHANGELOG.

## Options to decide between

- **A. Enter moves focus into the panel** (its heading or first action); Escape / close returns to the row. One keystroke
  from row to panel; focus moving on Enter is what a dialog does, and it may surprise someone who meant to keep browsing
  rows with the arrows.
- **B. The detail panel comes right after the table's row stop in Tab order** (before the page-size select and pager),
  focus staying on the row. Enter keeps its "open" meaning, one Tab reaches the panel; reordering Tab without moving the
  DOM needs care (a roving stop or `tabindex` order) and must not break the pager's own order.
- **C. A pane shortcut (F6, as Windows and Office move between panes)** plus A or B. Expected by power users, invisible
  to everyone else without a hint.

## Decision

**A**, taken when the user said "start 992" without choosing, after **A** had been recommended: a row opened from the
keyboard is something the user asked to see, so focus following it is what they expect, and it is one key. **B** lost
because reordering Tab apart from the DOM is fragile (a roving stop across the pager and the panel) and Enter would
still not take you there. **C** (F6) is not ruled out but adds nothing without A or B, and nobody finds it unhinted;
left for later if a consumer asks.

## Evidence

Birko.Web `task/TASK-992` ceca9a2 (starts v0.5.7), Playground 26740b7. `BaseSplitPage` reads on `row-click` whether
the row had keyboard focus (`:focus-visible`); after the panel is filled it waits one frame (`b-split-panel` reveals
its detail column from a MutationObserver, so before that nothing in it has layout or can take focus; measured: focus
stayed on the row without the wait) and focuses the panel's first control (`focusFirstIn`, which looks into shadow
roots, since a `b-button` host does not take focus), else its close button; a newer selection meanwhile wins. Escape
inside the panel (one nothing inside used) and the close button close it and call `focusRow(id)` (TASK-989). A click
does not move focus. `deepActiveElement` moved to `pages/focus-utils.ts`, shared with `BaseCrudPage`.
- `table-rows-keyboard-check` 23/23: Enter on row Bravo focuses Edit; one Tab reaches Delete; Escape closes and focus
  is on Bravo; reopened, Shift+Tab reaches close, Enter closes and focus is on Bravo; a click on Charlie opens it
  without moving focus into the panel. **Red** 5 of the 6 new on Birko.Web main (the click case holds either way).
- verify 0 failing; keyboard-check 249/249 (248 once, the TASK-985 flake, 249 on the re-run); a11y-name 43 OK; Shell
  `tsc` clean.

## Out of scope

- The table's own keyboard model (TASK-978) and focus after Undo (TASK-989), both done.

## Human test plan

- [ ] On Symbio HR → Positions, keyboard only: open a position with Enter, reach Delete in the fewest keys the chosen
      option promises, close the panel, focus is back on the row.
