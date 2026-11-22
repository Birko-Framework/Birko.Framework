---
id: TASK-964
parent: EPIC-001
feature: null
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

# b-kanban: a drop below a column's first card lands second

## Context

Found 2026-11-22 at TASK-961; older than v0.4.21 (v0.4.20 has the same loop). Working out where a dragged card goes,
the board looked at the column's cards in order and stopped at the first one: any card the pointer was below counted
as "drop after this card". So a drop on the third card, or below the last, landed right after the first card, and the
drop line showed there too. Separately, "before" / "after" a card used its index in the column's whole card list, sub-cards
shown above included, not its place among its siblings. It went unnoticed because DraCode turns reordering off, Symbio
does not reorder, and the gallery's lanes hold at most two cards.

## Acceptance criteria

- [x] A drop on a card's top edge lands before that card, on its bottom edge after it, and below the last card at the
      end, wherever the card is in the column; the drop line shows the same place.
- [x] With sub-cards shown above, before/after a card is counted among that card's siblings.
- [x] Playground check: red before, green after.

## Evidence

Birko.Web `task/TASK-961` 9c62d34 (v0.4.22), Playground 096d8a9: backport-smoke, 2 TASK-964 checks, both red on
v0.4.21 (a drop on the third card's top edge landed after the first, the line shown "after a"; with sub-cards above,
a drop before C landed after A), green after. Every earlier kanban drag check still passes (479/479).

## Human test plan

1. In the Playground gallery's kanban, set "reordering off in" to "in progress", then drag cards within To do's lanes
   onto the top or bottom edge of each card, and below the last one: each lands where dropped, and the line shows it
   there while dragging.
