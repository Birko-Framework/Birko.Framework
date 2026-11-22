---
id: TASK-969
parent: EPIC-001
feature: FEATURE-023
status: in-progress
priority: P2
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: a locked column shows only the lock icon

## Context

FEATURE-023 D2 (DraCode): a locked column's header shows a lock icon and a "Locked" chip, which repeats on a board with many locked columns. On every board it now shows only the icon; screen readers still hear "Locked" and pointing at it shows it. Built on `task/TASK-968` for v0.5.2.

## Acceptance criteria

- [ ] A locked column's header (columns and rows layouts) shows the lock icon without the word.
- [ ] The icon has the accessible name "Locked" (translatable, `bwc.kanban.locked`) and a tooltip.
- [ ] Playground check; a11y-name check still passes.

## Human test plan

1. In the gallery's kanban, the locked Done column's header shows a lock icon without the word "Locked".
2. Point at the icon: a "Locked" tooltip.
