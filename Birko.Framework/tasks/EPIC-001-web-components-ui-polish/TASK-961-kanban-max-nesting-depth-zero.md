---
id: TASK-961
parent: EPIC-001
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: maxNestingDepth 0 does not turn nesting off, and a drop nests past the limit

## Context

Reported by DraCode (2026-11-22, its TASK-183), on Birko.Web v0.4.21. `maxNestingDepth: 0` is read as "no limit", so
every card keeps an empty sub-card spacer strip at its left (visible in DraCode's screenshot of its board). Found while
checking it: a card dropped on the middle of another card becomes its sub-card whatever `maxNestingDepth` says, so the
limit only hides the toggle; it never stopped nesting.

## Acceptance criteria

- [x] `maxNestingDepth: 0`: no card can have sub-cards; no spacer, no toggle; a drop on a card's middle does not nest
      (it places the card before or after it).
- [x] `maxNestingDepth: n`: a drop that would put a card deeper than n does not nest it.
- [x] No `maxNestingDepth`: unchanged (unlimited).
- [x] Playground check: red before, green after.

## Evidence

Birko.Web `task/TASK-961` 9c62d34 (v0.4.22), Playground 096d8a9:
- backport-smoke, 3 TASK-961 checks. On v0.4.21 2 failed: with `maxNestingDepth: 0` the board showed 3 sub-card
  strips and a drop on A's middle made C its sub-card; with `maxNestingDepth: 1` a card dropped on a sub-card's middle
  and a card with sub-cards dropped on another's middle both nested past the limit. The baseline (no limit: a drop on a
  card's middle nests) held. After: all pass.
- The checks first came out wrong for another reason: every drop resolved against the column's first card. That is
  TASK-964, fixed in the same commit.
- Full Playground run: backport 479/479, keyboard 244/244, rerender-leak PASS, the drag suites; tsc clean.

## Human test plan

1. In the Playground gallery's kanban, set "nesting" to "off": cards have no strip at their left; dropping a card on
   the middle of another puts it beside it, not inside.

## Implementation plan

1. Treat `maxNestingDepth` 0 as no nesting: `canNest` is `maxDepth === undefined || depth < maxDepth`.
2. On drop, the middle zone nests only when the target card's depth + 1 (plus the dragged card's own sub-card depth)
   stays within the limit; otherwise the middle zone splits into before/after.
3. The gallery gets a nesting switch. README.
