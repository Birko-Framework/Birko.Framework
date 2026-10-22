---
id: TASK-731
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-732, TASK-736, TASK-741, TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: arrow-key navigation between commands and groups

## Context

FEATURE-019 D2 (approved by the user 2026-10-22): Inside the open ribbon, the arrow keys move between commands and from one group to the next, the way Office does.

Today ArrowDown on a tab moves focus to the first command and Escape returns to the tabs, but nothing moves between commands: every command is its own Tab stop, so crossing a ribbon takes dozens of Tab presses.

## Acceptance criteria

- [ ] Left/Right move to the previous/next command across group boundaries; Up/Down move within a column of a medium or small group
- [ ] Home/End go to the first/last command of the panel; Escape returns to the active tab
- [ ] The panel is one Tab stop (roving tabindex), and focus survives a re-render and a scaling change
- [ ] Folded (popup) groups are one stop; Enter/Space/ArrowDown opens the flyout and arrows work inside it
- [ ] keyboard-check.mjs covers it with real key presses
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Keyboard only: Tab into the ribbon, cross every group with the arrows, open a folded group, Escape back to the tabs
- [ ] NVDA or Narrator announces each command and its group
