---
id: TASK-978
parent: EPIC-001
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-11-24
depends-on: []
blocks: [TASK-545]
related: [TASK-545, TASK-974]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A table row cannot be opened by keyboard, so a page whose actions live in the row's detail panel is mouse-only

## Context

Found at TASK-545's keyboard step, on Symbio HR → Positions (2026-11-24). `b-table` emits `row-click` only from a
mouse `click` on a `tbody tr[data-id]`. The rows have no `tabindex`, no role, and no key handling, so:

- Tab goes from the column header (the sort button) straight to the pagination's page-size select; no row is reached.
- Arrow keys on the header do nothing.

Every `base-crud-page` / `base-split-page` that opens a detail panel on `row-click` is therefore unusable by keyboard:
its Edit and Delete live in that panel. Symbio's Positions is one of them (its Delete is there), and that is what
blocks TASK-545's "keyboard only" step. Pages with a per-row action column or row menu are not affected (the
Playground undo page has a row menu, which is why the keyboard Undo passed there). WCAG 2.1.1 (Keyboard).

## Acceptance criteria

- [ ] A table whose rows open something (`row-click` has a listener, or a config flag such as `rowsActivatable`;
      decide which while planning, preferring no consumer change) puts its rows in the keyboard order with a roving
      tabindex: one tab stop for the body, the remembered row or the first one.
- [ ] On a focused row, Enter (and Space) emit `row-click` with the same detail a click gives; Up/Down move between
      rows, Home/End to the first/last; the focused row has a visible focus ring in light and dark themes.
- [ ] Keys pressed inside an interactive element in a row (button, link, input, select, row menu) keep their own
      meaning and never also open the row, as the click path already ensures.
- [ ] The row's role and name let a screen reader say what Enter will do (e.g. the row is announced with its first
      cell's text); a table with no row action keeps its rows out of the tab order, as today.
- [ ] `b-data-table` (selection, bulk bar, Escape) keeps working; selection and row activation do not fight over
      Space if the table is `selectable` (settle which key does what in the plan).
- [ ] Playground check proving each of the above, red before the change; keyboard-check stays green.
- [ ] README (Components, `b-table` / `b-data-table`) documents the keys; CHANGELOG records it.

## Out of scope

- Moving focus after a delete or an Undo (TASK-974).
- Grid-style cell navigation (arrow keys between cells).

## Human test plan

- [ ] Symbio HR → Positions with the keyboard only: Tab into the table, arrow to a position, Enter opens its detail
      panel, Tab to Delete, Enter; reach the toast's Undo and press Enter; the row comes back and survives a reload
      (this is TASK-545's keyboard step).
- [ ] The same table in the dark theme: the focused row is clearly visible.
