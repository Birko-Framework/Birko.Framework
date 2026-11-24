---
id: TASK-974
parent: EPIC-001
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-24
depends-on: []
blocks: []
related: [TASK-545]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Undo delete: keyboard focus falls to the page after Undo

## Context

Found on 2026-11-24 while running TASK-545's human test plan in Chrome with real keys, on the Playground's `?kb=1`
undo-mode page (Birko.Web v0.5.3). A keyboard user deletes a row from its row menu: the row goes and focus moves to the
toast's Undo, as TASK-545 designed. Pressing Enter restores the row, but the toast closes with focus inside it and
focus falls to `<body>`. The user is back at the top of the page and has to Tab through everything to return to the
table. TASK-545 says where focus goes when the delete starts, and nothing about where it goes after Undo or when the
toast closes while it holds focus.

The restore runs in `base-crud-page`'s `_restore` (Undo's `onClick`), followed by `_afterRestore`, which reloads the
table by default. So the restored row's controls are new elements, and focus cannot simply go back to the old ones.

## Acceptance criteria

- [ ] After a keyboard Undo, focus lands on the restored row (its row menu or the row itself, whichever the table
      makes focusable), once the table has redrawn it.
- [ ] If the restored row is not on screen after the reload (another page, filtered out), focus goes to the table, not
      to `<body>`.
- [ ] When the toast closes on its own or by its × while it holds focus (no Undo), focus goes to the table, not to
      `<body>`.
- [ ] A pointer user's focus is left alone, as at the start of the delete.
- [ ] Playground check with trusted keys (`keyboard-check.mjs`), proven red against the current behaviour.

## Out of scope

- Where focus goes after a confirm-mode delete (no toast): not reported.
- The toast's screen-reader announcement (TASK-545's NVDA step).

## Implementation plan

1. Record in `_deleteWithUndo` that the delete came from the keyboard (it already reads `fromKeyboard`) and pass it on.
2. In `_restore`, after `_afterRestore(id)` resolves, when it came from the keyboard, focus the restored row's
   focusable control through the table (a method on the table that focuses a row by id, falling back to the table).
3. In the toast item, when it closes while it holds focus and nobody moved focus elsewhere, let the caller choose
   where it goes (a close callback or event); base-crud-page sends it to the table.
4. Extend `keyboard-check.mjs`'s TASK-545 block: after Enter on Undo, the active element is in the restored row; after
   a focused toast times out, it is in the table.

## Human test plan

1. In a consumer page with the undo opt-in, keyboard only: delete a row from its row menu, press Enter on Undo. Focus is
   on the restored row, and one Tab continues from there.
2. Delete again by keyboard and let the toast time out without pressing Undo. Focus is in the table.
