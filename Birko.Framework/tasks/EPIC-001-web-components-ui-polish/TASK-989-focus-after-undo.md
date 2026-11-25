---
id: TASK-989
parent: EPIC-001
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: [TASK-545, TASK-978]
findings: []
pr: null
github-issue: null
jira-key: null
---

# After a keyboard Undo, focus drops to the page body

## Context

Seen twice at TASK-545's keyboard test: on the Playground's undo-mode page (2026-11-24) and on Symbio HR → Positions
(2026-11-25, Symbio's run). Deleting a row with the keyboard moves focus to the toast's Undo, as designed; pressing
Enter restores the row, but the toast holding focus is replaced by the "restored" toast, so focus falls to `<body>`
and a keyboard user loses their place. TASK-545 did not say where focus should go.

## Acceptance criteria

- [ ] After Undo, focus goes to the restored row in the table (via TASK-978's roving tab stop) when the page shows one,
      otherwise back to where the delete was started; never to `<body>`.
- [ ] Decide and document what a page without a table gets.
- [ ] Playground check (the `?kb=1` undo-mode page): after Enter on Undo, the focused element is the restored row; red
      before the change.

## Out of scope

- Focus after a delete that is not undone (already goes to Undo).

## Human test plan

- [ ] On Symbio HR → Positions, keyboard only: delete a position, Enter on Undo; focus is on the restored row and the
      arrow keys keep working from there.
