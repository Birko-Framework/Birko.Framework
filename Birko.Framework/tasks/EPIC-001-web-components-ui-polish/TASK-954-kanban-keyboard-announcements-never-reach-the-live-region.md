---
id: TASK-954
parent: EPIC-001
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-21
depends-on: [TASK-947]
blocks: []
findings: [FIELD-055]
pr: null
github-issue: null
jira-key: null
---

# `b-kanban`: picking up and moving a card by keyboard is never announced

## Context

Found while building [[TASK-947]], 2026-11-21, and measured in the Playground (Birko.Web task/TASK-947 build):
`_startKeyboardDrag` and `_keyboardMoveCard` set the `_liveText` field but do not re-render or write the live region,
so its text stays as it was. Measured: after Space (pick up) the live region is empty; after ↓ it is still empty;
after → it reads "Column: In progress, position: 2" (that path now writes the region directly, since TASK-947); after
Escape it reads "Drag cancelled." (that path re-renders). A screen-reader user is told nothing when picking a card up
or moving it within a column.

The board's own announcements are also hard-coded English ("Picked up card: …", "Card position: …", "Column: …",
"Card dropped.", "Drag cancelled."), unlike the `bwc.kanban.*` strings TASK-947 added.

## Acceptance criteria

- [ ] Every keyboard step (pick up, ↑/↓, ←/→, drop, cancel) writes its announcement to the live region at once.
- [ ] Those announcements go through `bwc.kanban.*` keys with the current English as the fallback, in `en.json`.
- [ ] A Playground check reads the live region after each step; it fails before the fix.

## Out of scope

- The wording of the announcements beyond moving them to keys.

## Human test plan

1. With NVDA (or Narrator), Tab to a card on the Playground's kanban, press Space, ↓, →, Enter: each step is read
   out (picked up, the new position, the new column, dropped).
