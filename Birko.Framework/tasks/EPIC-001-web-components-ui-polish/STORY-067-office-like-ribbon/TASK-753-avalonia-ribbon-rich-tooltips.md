---
id: TASK-753
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-751]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: rich tooltips (name, description, shortcut)

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-738. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonItem.Description` and `Shortcut`; the tooltip shows the name, the description under it and the shortcut, in the light tooltip look; it shows on hover after a delay and at once on keyboard focus; Escape hides it first; the shortcut is exposed to automation.

## Acceptance criteria

- [ ] `RibbonItem.Description` / `Shortcut` in Birko.Xaml.Core
- [ ] Tooltip with name, description and shortcut in the light look; hover delay, at once on keyboard focus, Escape hides it
- [ ] Automation name and help text set; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Hover and keyboard-focus commands in the Gallery: the tooltip shows name, description and shortcut, and Escape hides it
