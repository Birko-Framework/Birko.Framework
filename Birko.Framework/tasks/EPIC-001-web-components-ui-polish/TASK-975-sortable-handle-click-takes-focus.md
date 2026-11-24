---
id: TASK-975
parent: EPIC-001
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-11-24
depends-on: []
blocks: []
related: [TASK-787]
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-sortable-list: clicking a drag handle does not focus it

## Context

Found on 2026-11-24 while running TASK-787's human test plan in Chrome with real clicks and keys (Birko.Web v0.5.3).
A mouse click on a row's drag handle leaves focus where it was (in the test, on the gallery's ribbon), so Space and the
arrow keys that follow act on that element instead of moving the row. Reaching the handle with Tab works. A user who
clicks a handle and then reaches for the keyboard, or a screen-magnifier user who points and then types, gets nothing.

The cause is in `PointerDrag` (`pointer-drag.ts`): its `pointerdown` handler calls `preventDefault()` so a drag selects
no text and does not start a native image drag, and that also stops the browser focusing the button. It is shared, so
`b-kanban` and the tree's drag may behave the same way.

## Acceptance criteria

- [ ] A click on a `b-sortable-list` handle focuses that handle (no visible focus ring for a pointer click, as usual
      for `:focus-visible`), and Space then picks the row up.
- [ ] A drag still selects no text and starts no native image drag.
- [ ] The other `PointerDrag` users (`b-kanban` cards, the tree) are checked: each either focuses the pressed item too,
      or the task records why it should not.
- [ ] Trusted-input check (`sortable-drag-check.mjs`): after a click on a handle, the active element is that handle.

## Out of scope

- Changing which element is focusable in a row, or the keyboard model itself (TASK-787).

## Implementation plan

1. In `PointerDrag._down`, after `preventDefault()`, focus the pressed focusable element with
   `focus({ preventScroll: true })`, or let each user supply the element to focus.
2. Add the check to `sortable-drag-check.mjs`; check `b-kanban` and the tree with the same click.

## Human test plan

1. Playground → Data → Sortable list: click a row's handle, then press Space, Down, Space. The row moves down one.
