---
id: TASK-757
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-23
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: collapse by double-clicking a tab

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-735. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

Ctrl+F1 already collapses the desktop ribbon. Add Office's double-click on a tab, with the same toggle semantics as the web (pinned ↔ tabs only), and make the collapse shortcut rebindable or off.

## Acceptance criteria

- [ ] Double-clicking a tab collapses the ribbon and brings it back
- [ ] Ctrl+F1 and the double-click share one toggle; the shortcut is rebindable or off
- [ ] Headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Double-click a tab and press Ctrl+F1 in the Gallery: the ribbon collapses and comes back
