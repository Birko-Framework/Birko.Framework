---
id: TASK-949
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

# b-kanban: reordering off per column, and the keyboard across lanes

## Context

FEATURE-022 D7 and D9 (approved by the user 2026-11-21):
- D7: a column can turn reordering off: a card dropped there takes the place the app gives it, and dragging within it
  does nothing.
- D9: with the keyboard, Left and Right move a picked-up card between columns as now; Up and Down move it within a lane
  and on into the lane above or below (straight to the next lane where reordering is off). Locked columns and refused
  places are skipped, and the screen reader names the lane.

Decisions: [FEATURE-022](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md).

## Acceptance criteria

- [ ] A column with reordering off shows no drop position between cards; a dropped card takes the app's order; a drag
      within the same lane or column changes nothing and reports nothing.
- [ ] Columns keep reordering unless they turn it off.
- [ ] Keyboard: Up/Down cross lanes (within a lane first where reordering is on); Left/Right cross columns, skipping
      locked columns and places the move rule refuses, and saying which were skipped.
- [ ] The live announcement names the column and the lane.
- [ ] Playground checks (including the existing keyboard-check) red before, green after.

## Human test plan

1. On a board whose To-do column has reordering off, drag a card within its lane: nothing changes. Drag it to
   another lane: it lands where the app's order puts it.
2. Tab to a To-do card, press Space, then Up/Down: the card steps from lane to lane, and a screen reader (or the live
   region) names each lane. Press Enter to drop, or Escape to cancel.
3. Press Left/Right next to locked columns: they are skipped, and the message says so.
