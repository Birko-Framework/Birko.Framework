---
id: TASK-948
parent: STORY-070
feature: FEATURE-022
status: todo
priority: P2
assignee: ai
created: 2026-11-21
depends-on: [TASK-947]
blocks: [TASK-949, TASK-950]
pr: null
github-issue: null
jira-key: null
---

# b-kanban: lanes inside a column

## Context

FEATURE-022 D5 (approved by the user 2026-11-21): a column can hold lanes, one level deep, each with its own label
and card count and each a place to drop a card, and an empty lane stays visible as a place to drop; a move reports the
lane a card left and the lane it entered.

DraCode's next task splits "To do" into priority lanes (Critical, High, Normal, Low); moving a card between lanes
changes its priority. Lanes go on a column, and a card says which lane it is in (chosen over columns inside columns).
D12 (lane collapse and lane card limits) is deferred.

Decisions: [FEATURE-022](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md).

## Acceptance criteria

- [ ] A column can list lanes (an id, a label, an optional colour); a card names its lane.
- [ ] Each lane shows its label and its own card count, and is a drop target; an empty lane stays visible.
- [ ] A card dropped on a column with lanes but outside any lane lands in a defined lane (documented).
- [ ] A move between lanes, or into or out of a lane, is reported with the lane it left and the lane it entered; the
      lock and the move rule from TASK-947 see the lanes.
- [ ] Columns without lanes behave as before.
- [ ] Docs and CHANGELOG; Playground checks red before, green after.

## Human test plan

1. On a board whose To-do column has four lanes, check each lane shows its name and count, and an empty lane still
   shows a place to drop.
2. Drag a card from Normal to Critical: it moves, both counts change, and the page is told the old and new lane.
3. Drag a card from another column into To do: it lands in the documented lane.

## Implementation plan

Drafted 2026-11-21 on the integration branch `task/TASK-947` (b-kanban with TASK-947 and TASK-955).

**API (names for review):**
- `KanbanColumn.lanes?: KanbanLane[]`, `KanbanLane = { id, label, color? }`; `KanbanColumn.defaultLane?: string`.
- `KanbanCard.laneId?: string`. In a column with lanes, a card with no `laneId`, or one naming no lane of that
  column, shows in the default lane (`defaultLane`, else the first lane). Ignored in a column without lanes.
- `moveCard(id, column, index?, parent?, lane?)`: a fifth, optional argument; index counts within the lane.
- `KanbanMove` and the `card-move` / `card-reorder` / `card-move-request` detail gain `fromLane` / `toLane` when a
  column involved has lanes. `canMove` and the locks receive them.
- `getCards(columnId?, laneId?)`.

**Steps:**
1. One helper for a card's effective lane, and ordering per (column, lane, parent): `_getOrderedCards` takes the lane;
   `order` is renumbered within a lane.
2. Render: a laned column's body holds one `lane` section per lane (header: colour dot, label, count) whose body is
   the drop target (`.column-body` with `data-lane-id`, so the existing drag code applies). An empty lane keeps a
   minimum height and the empty text. The column count stays the column's total.
3. Drop: the lane comes from the drop target; a drop on a laned column outside any lane (not reachable with the
   mouse, but by keyboard and code) lands in the default lane.
4. Moves: `_applyMove` / `_userMove` / `revertMove` carry the lane; a lane change is reported (see the open decision).
5. Keyboard (enough for this task; TASK-949 adds ↑/↓ across lanes): entering a laned column targets its default lane;
   ↑/↓ move within the card's lane. The travelling card (TASK-955) goes into the lane's body.
6. Nested cards follow their parent's lane.
7. Docs: README (lanes), CHANGELOG under v0.4.21; the gallery kanban gets lanes in To do.

**Tests** (backport-smoke, red first): lanes render with label and count, an empty lane is visible; a card with no or an
unknown `laneId` shows in the default lane; a drag between lanes moves it and reports `fromLane` / `toLane`; `canMove`
sees `toLane` and can refuse it; `revertMove` restores the lane; `moveCard(…, lane)` works; a board without lanes moves
as before (the TASK-947 checks unchanged).

**Decided (the user, 2026-11-21):** a lane change within a column is a `card-move` (FEATURE-022 D13); a card with no
or an unknown `laneId` shows in the default lane (D14). And D5 changed: lanes have three display modes. This task
builds the lane model and the default mode, sections; rows are TASK-956 and tags TASK-957, which build on it. So the
model must not assume sections: the effective-lane helper, the lane-aware ordering and the move detail are shared.
