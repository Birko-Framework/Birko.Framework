---
id: TASK-525
parent: STORY-059
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-07
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-tabs drag-and-drop reorder; b-split-panel size persistence; a layout serialize/restore hook

## Context

DraCode TASK-058 needs drag-and-drop tab reordering and saved workspace layouts. `b-tabs` and `b-split-panel` have no
drag or persistence support today (only `b-kanban` drags), so the plan was to build them app-side.

Consumer side: DraCode `EPIC-018 / STORY-039` (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [ ] `b-tabs` can be made reorderable by drag (and by keyboard), emitting the new order
- [ ] `b-split-panel` exposes and accepts its sizes, and can persist them under a key
- [ ] A serialize/restore hook lets an app save a layout (tab order + split sizes) wherever it likes
- [ ] Component tests; accessible keyboard path

## Out of scope

- Server-side layout storage (apps)

## Human test plan

- [ ] Drag tabs and resize a split in the Birko.Web playground; reload restores the layout

## Implementation plan
