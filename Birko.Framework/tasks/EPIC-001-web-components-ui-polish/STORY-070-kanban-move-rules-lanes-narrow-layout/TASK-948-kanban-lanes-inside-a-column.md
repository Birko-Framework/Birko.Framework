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
