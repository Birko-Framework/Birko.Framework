---
id: TASK-956
parent: STORY-070
feature: FEATURE-022
status: done
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

**Decided (the user, 2026-11-21):** board-level lanes (D15) and a config field for the mode (D16).

## Acceptance criteria

- [x] A page can choose rows; each lane is a row with its label and count, crossing every column it applies to.
- [x] A card can be dragged and keyboard-moved to any column × lane crossing; moves report `fromLane` / `toLane`, and the
      locks and `canMove` (TASK-947) apply.
- [x] Sections stay the default; a board that does not choose rows looks as with TASK-948.
- [x] Docs and CHANGELOG; Playground checks red before, green after.

## Evidence

Birko.Web `task/TASK-947` c22631d (v0.4.21), Playground af0aa91:
- backport-smoke: 8 TASK-956 checks (row headers with labels and board-wide counts; a cell per column × row with the
  opted-out column as one cell spanning all three rows; cards in their cells, one with no lane in the default row; a
  drag to another column's row reporting `toColumn` and `toLane`; a locked column's cell refusing; the keyboard keeping
  the row; board-level lanes as sections with `lanes: []` opting out). All 8 fail on the branch before the change;
  441/441 after. (The two commits say "9"; it is 8.)
- keyboard-check 237/237; verify 0 failing; a11y-name OK; rerender-leak PASS; entry-points 433/433; locale-keys 23/23;
  device-fix 245/245; Components `tsc` clean.
- In Chrome, the gallery kanban's new "lanes" switch turns the board into a 3-row grid (Task B in the High row) and
  back into sections.

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22, the human test plan below run in the Playground
from the `task/TASK-947` build: "All passed".

## Human test plan

1. In the Playground, switch the kanban to rows: the lanes run across all columns, each with its label and count.
2. Drag a card to another row and another column, and do the same by keyboard: it lands at that crossing.

## Implementation plan

Drafted 2026-11-21 on the integration branch `task/TASK-947`.

**API:** `KanbanConfig.lanes?: KanbanLane[]` and `KanbanConfig.defaultLane?` (board-wide); `KanbanConfig.laneLayout?:
'sections' | 'rows' | 'tags'` (default `'sections'`). A column's lanes are its own `lanes` when set (`[]` = none),
else the board's. In rows mode the rows are the board's lanes, and a column with `lanes: []` is one tall cell.

**Steps:**
1. Lane resolution moves to one place (column's own, else the board's); the default lane: the column's
   `defaultLane`, else the board's, else the first lane.
2. Moving into another column keeps the card's lane when that column has it (the row stays the row), else the
   default lane, in every mode.
3. Rows render as a grid: a header row (column title, lock, count), then one row per lane (label, colour, count across
   the board), with one cell per column × lane. Each cell is a drop target (`.column-body` with column and lane) inside
   a `.column` cell wrapper, so locks, refusal marks, the travelling card and keyboard focus keep working. An opted-out
   column is one cell spanning all rows.
4. Keyboard: ←/→ keep the row; focus moves to a card in the same row of the next column; ↑/↓ cross rows (TASK-949).
5. Docs, CHANGELOG; the gallery gets a switch between sections and rows.

**Tests** (red first): rows render (row labels and counts, a cell per column × lane, the opted-out column spanning
all rows, cards in their cells); a drag to another column's row moves both and reports `toLane`; a locked column's
cell refuses; the keyboard keeps the row across columns; sections with board-level lanes; a board with no lanes
unchanged.
