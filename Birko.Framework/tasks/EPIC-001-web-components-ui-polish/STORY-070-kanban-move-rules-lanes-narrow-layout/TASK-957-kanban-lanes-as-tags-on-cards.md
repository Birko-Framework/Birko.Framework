---
id: TASK-957
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

# b-kanban: lanes shown as a tag on each card

## Context

FEATURE-022 D5, changed 2026-11-21 by the user ("all 3 as option"): besides sections (TASK-948, the default) and rows
(TASK-956), a page can show lanes with no lane areas: the cards stay one list per column, each carrying its lane as a
small tag. Decisions: [FEATURE-022](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md).

## Acceptance criteria

- [ ] A page can choose tags; every card in a laned column shows its lane's label (and colour) as a tag.
- [ ] The lane can be changed from the tag's menu (D17), and by keyboard by moving a picked-up card past its lane's end;
      the change is reported with `fromLane` / `toLane`.
- [ ] Cards are grouped by lane in lane order, with no headings (D18).
- [ ] A board that does not choose tags looks as with TASK-948.
- [ ] Docs and CHANGELOG; Playground checks red before, green after.

## Human test plan

1. In the Playground, switch the kanban to tags: the To-do cards show their lane as a tag, and there are no lane
   sections.
2. Change a card's lane: its tag changes and the page is told the old and new lane.

## Implementation plan

Drafted 2026-11-21 on the integration branch `task/TASK-947`. Decided (the user): D17 (the tag opens a lane menu), D18
(grouped by lane).

1. `laneLayout: 'tags'`: each column has one drop body (no lane sections); its cards show grouped by lane in lane order,
   then by `order`. A top-level card in a laned column carries a tag button (dot, lane label, ▾) with
   `aria-haspopup="menu"` and `tabindex="-1"`, so the board stays one tab stop.
2. Clicking the tag opens a small menu (`role="menu"`, one `menuitemradio` per lane, the current one checked) under it;
   ↑/↓ move in it, Enter picks, Escape or a click outside closes and returns focus. A pick goes through the person
   funnel (`_userMove`: locks, `canMove`, `card-move-request`), so a refusal is said and the card stays.
3. The tag never starts a drag or fires `card-click` (the tag only; TASK-946 makes this general).
4. Keyboard: a picked-up card's ↑/↓ cross lane boundaries in the grouped list (TASK-949's logic), the travelling card
   shown at its grouped position; Enter drops it in the lane reached.
5. A drop in tags mode lands at the end of the card's lane in that column (there is no lane under the pointer).
6. Docs, CHANGELOG; the gallery's lanes switch gains "tags".

**Tests** (red first): tags render (no sections, one body per column, a tag per laned card, the default lane's tag
for a card with none), grouped order; the menu (opens, lists lanes with the current checked, a pick moves the card and
reports the lanes, Escape closes with no change, a refused lane is said); the tag not firing `card-click` nor starting
a drag; the keyboard crossing a lane boundary.
