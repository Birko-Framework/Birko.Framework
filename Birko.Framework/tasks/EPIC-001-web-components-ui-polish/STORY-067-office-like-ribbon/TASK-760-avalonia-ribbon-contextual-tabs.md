---
id: TASK-760
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

# Avalonia ribbon: contextual tabs

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-734. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonTab.ContextualSet` with show / hide; the three looks (band, pill, edge) the user kept, in theme colours; hiding the open set returns to the previous normal tab; the set's name is announced with its tabs.

## Acceptance criteria

- [ ] Show / hide contextual sets; band / pill / edge, switchable; theme colours
- [ ] Hiding the open set returns to the previous normal tab
- [ ] Automation names the set; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Show and hide the Table set in the Gallery in all three looks and every theme
