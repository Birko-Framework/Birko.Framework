---
id: TASK-949
parent: STORY-070
feature: FEATURE-022
status: in-progress
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

## Implementation plan

Drafted 2026-11-21 on the integration branch `task/TASK-947` (with TASK-947, TASK-948, TASK-955).

**API:** `KanbanColumn.sortable?: boolean` (default `true`). No other new surface.

**Reordering off (D7), for a column with `sortable: false`:**
1. The board never renumbers `order` there: cards show in the app's `order` (DraCode's agents decide it).
2. A card dropped there takes its place by its own `order`, whatever the drop position; no drop line is drawn while
   dragging over it (the column / lane is still highlighted).
3. A drag within the same lane (or the same lane-less column) changes nothing and fires nothing.

**Keyboard across lanes (D9):**
4. ↑/↓ in a laned column: with reordering on, move within the lane and, at its edge, on into the lane above (at its
   end) or below (at its start); with reordering off, straight to the next lane. Lanes the move rule refuses are
   skipped and named, like columns (TASK-947). In a column without lanes and with reordering off, ↑/↓ says so.
5. The travelling card (TASK-955) follows; the announcement names the column, the lane and the position, through
   `bwc.kanban.*` keys.

**Tests** (red first): backport-smoke for the drops and `order` (synthetic drags), the keyboard lane crossing in both
modes, a refused lane skipped, and reordering-off said in a lane-less column; keyboard-check with real keys for one
lane crossing. Boards that set nothing behave as before (the TASK-947 / TASK-948 checks unchanged).
