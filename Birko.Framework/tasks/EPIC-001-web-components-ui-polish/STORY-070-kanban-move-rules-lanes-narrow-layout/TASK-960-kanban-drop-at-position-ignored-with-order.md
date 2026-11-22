---
id: TASK-960
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

# b-kanban: a card dropped at a position among ordered cards goes back where it was

## Context

Found 2026-11-22 testing the gallery's new reordering switch (asked for by the user: reordering within a lane).
Older than FEATURE-022: v0.4.20 has the same code.

A drop before or after a card put the moved card at that place in the board's card list, then sorted the group by the
cards' `order`. When cards carry their own `order` (as apps' and the gallery's do), the old values won and the card
returned to its place, while `card-reorder` / `card-move` reported the new index. Reordering only worked for cards with
no `order`.

## Acceptance criteria

- [x] A card dropped before or after another, in a column the board orders, lands there and its group is numbered in
      the new order, also within a lane and when it comes from another column.
- [x] The reported `toIndex` matches where the card is.
- [x] Playground check: red before, green after.

## Evidence

Birko.Web `task/TASK-947` 6f483b4 (v0.4.21), Playground 12a5fef:
- backport-smoke: 2 TASK-960 checks with explicit `order` values. Before: dropping C before A left A, B, C (reported
  toIndex 0); in a lane, E before A and X from another column before E gave X, A, E. After: C, A, B and X, E, A;
  471/471, 0 failing checks.
- keyboard 244/244; a11y-name OK; rerender-leak PASS; entry-points 433/433; locale-keys 23/23; device-fix 247/247;
  contextual-ribbon 13/13; Components `tsc` clean. All from builds that printed "✓ Built".
- Chrome, the gallery, synthetic drag events at the real coordinates: Task E dropped on Task A's top edge was accepted
  and, before the fix, stayed below it (A 0, E 1). The Chrome tool's own mouse drag never delivers a drop on a card's
  top edge (its last dragover stops short), so a real-mouse check is in the human test plan.

## Human test plan

1. In the Playground gallery's kanban (Ctrl+Shift+R first), set "reordering off in" to "in progress".
2. Drag Task E onto the top edge of Task A in To do's Critical lane: E is now above A, and stays there.
3. Drag a card from In progress onto the top edge of a To do card: it lands above that card, in that lane.
4. Set the switch back to "to do": dragging within a To do lane changes nothing (TASK-949), and nothing stays gray.
