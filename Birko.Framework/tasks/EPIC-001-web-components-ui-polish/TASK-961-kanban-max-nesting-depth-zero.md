---
id: TASK-961
parent: EPIC-001
feature: null
status: in-progress
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

- [ ] `maxNestingDepth: 0`: no card can have sub-cards; no spacer, no toggle; a drop on a card's middle does not nest
      (it places the card before or after it).
- [ ] `maxNestingDepth: n`: a drop that would put a card deeper than n does not nest it.
- [ ] No `maxNestingDepth`: unchanged (unlimited).
- [ ] Playground check: red before, green after.

## Human test plan

1. In the Playground gallery's kanban, set "nesting" to "off": cards have no strip at their left; dropping a card on
   the middle of another puts it beside it, not inside.

## Implementation plan

1. Treat `maxNestingDepth` 0 as no nesting: `canNest` is `maxDepth === undefined || depth < maxDepth`.
2. On drop, the middle zone nests only when the target card's depth + 1 (plus the dragged card's own sub-card depth)
   stays within the limit; otherwise the middle zone splits into before/after.
3. The gallery gets a nesting switch. README.
