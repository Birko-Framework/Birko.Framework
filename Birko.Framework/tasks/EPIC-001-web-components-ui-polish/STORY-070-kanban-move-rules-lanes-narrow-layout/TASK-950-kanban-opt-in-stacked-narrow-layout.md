---
id: TASK-950
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

# b-kanban: an opt-in stacked layout for a narrow board

## Context

FEATURE-022 D6 (approved by the user 2026-11-21): a board stacks its columns one under another, with no sideways
scrolling, only when the page asks for it and only when the board itself is narrower than the width the page gives;
lanes stay grouped under their headings when stacked.

Measured on the board's own width (a container query), so a board beside a docked chat stacks too. Without the opt-in
nothing changes, so Symbio's three boards keep scrolling sideways.

Decisions: [FEATURE-022](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md).

## Acceptance criteria

- [ ] A page can opt in with a width; below it the board stacks one column per row with no horizontal scroll.
- [ ] It follows the board's own width, not the window's.
- [ ] Lanes stay grouped under their headings when stacked.
- [ ] Without the opt-in the board looks and scrolls exactly as before.
- [ ] Drag and keyboard moves still work when stacked.
- [ ] Checked at phone width and beside a narrow container (device-fix-check or an equivalent), in light and dark.

## Human test plan

1. On a board that opts in, narrow its container below the width: columns stack, with no sideways scroll, and the
   To-do lanes stay grouped under their headings.
2. Widen it again: the columns sit side by side.
3. Open one of Symbio's boards on a phone: it still scrolls sideways (it has not opted in).
