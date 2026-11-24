---
id: TASK-978
parent: EPIC-001
feature: null
status: done
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

- [x] A table whose rows open something puts its rows in the keyboard order with a roving tabindex: one tab stop for
      the body, the row last focused, else the active row, else the first. Decided: an attribute,
      `<b-table clickable-rows>` (`b-data-table`: `clickableRows`), set by the Shell, not detected from listeners: a
      split page always sets it (a row opens its detail), a list page when it overrides `onRowClick`. No consumer
      change.
- [x] On a focused row, Enter and Space emit `row-click` with the same detail a click gives; Up/Down move between
      rows, Home/End to the first/last; the focused row has a focus ring (2px primary outline) in light and dark.
- [x] Keys pressed inside a control in a row keep their own meaning and never also open the row (the handler acts
      only when the row itself is the key's target).
- [x] A table with no row action keeps its rows out of the tab order, as today. A focused row is a `row` with its
      cells' text, which is what a screen reader reads; no extra name was added.
- [x] `b-data-table` selection keeps working. Decided: Space on a row with a checkbox toggles that checkbox (the
      selectable table's case); Enter opens the row.
- [x] Playground check proving each of the above, red before the change; keyboard-check stays green.
- [x] README (Components `b-table` / `b-data-table`, Shell hooks) documents the keys; CHANGELOG records it.

Also: a re-render (a reload, new data, a sort) while a row has focus puts focus back on that row; the positional DOM
morph would otherwise leave it on whatever row now sits in that place.

## Evidence

Birko.Web `task/TASK-978` f1a5f4e (joins the unpushed v0.5.3), Playground 0d0169d. All from builds that printed
"✓ Built".
- `table-rows-keyboard-check` 17/17 (trusted keys): a split page in undo mode laid out like Symbio's Positions is
  opened, deleted and restored by keyboard only (Tab to the rows, arrows, Enter opens the detail, Tab to Delete,
  Enter, Enter on Undo); a reload keeps row focus; list pages with and without `onRowClick`; a selectable table's
  Space. **Proven red:** 4/17 against Birko.Web main (the four that hold either way), and the reload-focus check red
  (16/17) with only the re-focus removed.
- verify 0 failing; keyboard-check 244/244; a11y-name OK (43); rerender-leak PASS; escape-in-modal 27/27;
  locale-switch 47/47; entry-points 433/433; toast-announce 7/7; Core, Components and Shell `tsc` clean.
- Looked at in Chrome on `/?rows=show`: the focused row's outline is clear in the light and the dark theme.

## Out of scope

- Moving focus after a delete or an Undo (TASK-974).
- Grid-style cell navigation (arrow keys between cells).

## Human test plan

- [ ] (moved to TASK-545's keyboard step) Symbio HR → Positions with the keyboard only: Tab into the table, arrow to a position, Enter opens its detail
      panel, Tab to Delete, Enter; reach the toast's Undo and press Enter; the row comes back and survives a reload
      (this is TASK-545's keyboard step).
- [x] The same table in the dark theme: the focused row is clearly visible.

**Passed 2026-11-24** by the user with the keyboard on the Playground's `/?rows=show` page (a split page in undo
mode laid out like Symbio's Positions): rows reached by Tab, arrows between them, Enter opened the detail, Delete and
Undo by keyboard. The Symbio Positions run is the same flow on the consumer and is TASK-545's open keyboard step; it
needs Symbio's UI rebuilt from Birko.Web main.

**Signed off** by the user (František Bereň, maintainer) on 2026-11-24: "seems working". Merged into Birko.Web main
aa5d91c as part of v0.5.3 (the unpushed tag moved to the merge); `table-rows-keyboard-check` 17/17 and verify 0
failing on main.
