---
id: TASK-957
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

# b-kanban: lanes shown as a tag on each card

## Context

FEATURE-022 D5, changed 2026-11-21 by the user ("all 3 as option"): besides sections (TASK-948, the default) and rows
(TASK-956), a page can show lanes with no lane areas: the cards stay one list per column, each carrying its lane as a
small tag. Decisions: [FEATURE-022](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md).

## Acceptance criteria

- [x] A page can choose tags; every card in a laned column shows its lane's label (and colour) as a tag.
- [x] The lane can be changed from the tag's menu (D17), and by keyboard by moving a picked-up card past its lane's end;
      the change is reported with `fromLane` / `toLane`.
- [x] Cards are grouped by lane in lane order, with no headings (D18).
- [x] A board that does not choose tags looks as with TASK-948.
- [x] Docs and CHANGELOG; Playground checks red before, green after.

## Evidence

Birko.Web `task/TASK-947` 359792b (v0.4.21), Playground 5a93445:
- backport-smoke: 11 TASK-957 checks. Before the change: 2 failed (no tags; lane sections instead of one body), 1
  passed (grouped order, which sections already gave), and the other 8 did not run, because the missing menu threw
  ("11 red" in the commits overstates it). After: 452/452, 0 failing checks.
- One check was changed after the code: a move with no position now lands at the end of its lane (fixed here: it
  sorted by the card's old `order`), so the High lane holds H then N, and the keyboard check now presses ↓ twice
  (within the lane first, then across, D9) instead of once.
- keyboard-check 237/237; verify 0 failing; a11y-name OK; rerender-leak PASS; entry-points 433/433; locale-keys 23/23;
  device-fix 245/245; contextual-ribbon 13/13; Components `tsc` clean.
- Chrome, the gallery in tags mode, real click and keys: Task D's tag opens Critical / High / Normal (checked); ↑,
  Enter moves it to High, the menu closes and focus returns to the card.
- Two Playground builds on the way failed on an apostrophe in a gallery note and left the old bundle in place; all
  numbers above are from a build that printed "✓ Built".

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22, the human test plan below run in the Playground
from the `task/TASK-947` build: "All passed".

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
