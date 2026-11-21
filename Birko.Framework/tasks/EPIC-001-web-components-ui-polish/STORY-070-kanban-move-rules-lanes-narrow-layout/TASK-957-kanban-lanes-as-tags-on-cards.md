---
id: TASK-957
parent: STORY-070
feature: FEATURE-022
status: todo
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
- [ ] The lane can still be changed (how, without lane areas to drop on, is for this task's plan), and the change is
      reported with `fromLane` / `toLane`.
- [ ] A board that does not choose tags looks as with TASK-948.
- [ ] Docs and CHANGELOG; Playground checks red before, green after.

## Human test plan

1. In the Playground, switch the kanban to tags: the To-do cards show their lane as a tag, and there are no lane
   sections.
2. Change a card's lane: its tag changes and the page is told the old and new lane.
