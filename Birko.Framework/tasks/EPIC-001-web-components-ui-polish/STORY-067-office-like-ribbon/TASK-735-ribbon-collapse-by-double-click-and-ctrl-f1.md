---
id: TASK-735
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: collapse by double-clicking a tab, and Ctrl+F1

## Context

FEATURE-019 D6 (approved by the user 2026-10-22): Double-clicking a tab collapses or expands the ribbon, and Ctrl+F1 does the same, as in Office.

Only the ▲ button collapses the panel today.

## Acceptance criteria

- [ ] Double-click on a tab toggles expanded (and emits expand as the button does); a single click still selects
- [ ] Ctrl+F1 toggles while focus is inside the ribbon (decide whether it is app-wide: an app may own the shortcut)
- [ ] Documented, and the shortcut shown in the collapse button's tooltip
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Double-click tabs and press Ctrl+F1: the ribbon collapses and comes back, pinned state respected
