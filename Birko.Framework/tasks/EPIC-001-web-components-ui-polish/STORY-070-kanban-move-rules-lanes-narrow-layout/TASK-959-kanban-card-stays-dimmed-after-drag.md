---
id: TASK-959
parent: STORY-070
feature: FEATURE-022
status: verify
priority: P1
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: a card stays dimmed and the board stops responding after a drag

## Context

Found by the user testing FEATURE-022 in the Playground (2026-11-22): "when i dragen dthe card in kanbad and released
it stayed gray not able to work with it".

Two causes, both in how a drag ends:

- Drag end never removed the card's dimmed look; it relied on the board being redrawn by the move. A drag that ends
  without a redraw left the card dimmed: refused (a locked column), dropped in place where reordering is off (TASK-949),
  or released outside the board.
- The drag-end listener was one the board drops at every redraw. A drop that redraws the board therefore took the
  listener with it, the browser's drag end reached nothing, and the board kept thinking a drag was in progress, so it
  ignored every click on a card from then on.

## Acceptance criteria

- [x] However a drag ends (moved, in place, refused, outside the board), no card stays dimmed.
- [x] After any drag, a click on a card is a card click and the same card can be dragged again.
- [x] Playground check: red before, green after.

## Evidence

Birko.Web `task/TASK-947` 7411727 (v0.4.21), Playground 1e05da7:
- backport-smoke: 5 TASK-959 checks, one per way a drag ends. On the previous code 4 failed: three left the card
  dimmed, and the user's case in the gallery (a card from another column dropped into a lane with reordering off) left
  the board taking no card click at all. The fifth (a plain move to another column) held: its redraw kept the card
  element. After: 469/469, 0 failing checks.
- Chrome, the gallery board, a real mouse: a drag refused by the locked Done, a move into In progress and a move back
  into To do's Critical lane leave no card dimmed, and the next click is a card click. On the move back, the browser's
  drag end never reached the board (the card had been redrawn), the case that stuck the board before.
- keyboard 238/238; verify 0 failing; a11y-name OK; rerender-leak PASS; entry-points 433/433; locale-keys 23/23;
  device-fix 247/247; contextual-ribbon 13/13; Components `tsc` clean. All from builds that printed "✓ Built".

## Human test plan

1. In the Playground gallery's kanban (Ctrl+Shift+R first), drag Task A within its own To do lane and release: it is
   not gray. Click it: a "Card click" toast.
2. Drag it onto the locked Done column and release: refused, not gray, and it still clicks.
3. Drag it into In progress, then back into To do: each time it lands, is not gray, and the next click works.
4. Drag a card and release it outside the board: not gray.
