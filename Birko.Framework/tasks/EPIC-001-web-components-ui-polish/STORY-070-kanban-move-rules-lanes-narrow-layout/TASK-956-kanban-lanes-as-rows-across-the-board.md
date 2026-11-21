---
id: TASK-956
parent: STORY-070
feature: FEATURE-022
status: todo
priority: P2
assignee: ai
created: 2026-11-21
depends-on: [TASK-948]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: lanes shown as rows across the board (swimlanes)

## Context

FEATURE-022 D5, changed 2026-11-21 by the user ("all 3 as option"): besides sections inside a column (TASK-948, the
default), a page can show lanes as rows across the whole board, so every card sits at a column × lane crossing.
Decisions: [FEATURE-022](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md).

**Open decision, for this task's plan:** D5's lanes belong to one column (`KanbanColumn.lanes`), but rows across the
board need one set of lanes for every column. Board-level lanes, the laned column's lanes applied to all, or rows only
for the columns that list the same lanes?

## Acceptance criteria

- [ ] A page can choose rows; each lane is a row with its label and count, crossing every column it applies to.
- [ ] A card can be dragged and keyboard-moved to any column × lane crossing; moves report `fromLane` / `toLane`, and the
      locks and `canMove` (TASK-947) apply.
- [ ] Sections stay the default; a board that does not choose rows looks as with TASK-948.
- [ ] Docs and CHANGELOG; Playground checks red before, green after.

## Human test plan

1. In the Playground, switch the kanban to rows: the lanes run across all columns, each with its label and count.
2. Drag a card to another row and another column, and do the same by keyboard: it lands at that crossing.
