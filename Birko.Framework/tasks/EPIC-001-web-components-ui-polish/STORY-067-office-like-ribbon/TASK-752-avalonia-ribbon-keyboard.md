---
id: TASK-752
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

# Avalonia ribbon: the panel is one Tab stop and the arrows move by position

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-731. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

Roving focus: Tab reaches the panel once; ←/→ move to the nearest command that way across groups and wrap; ↑/↓ stay in the column, ↑ from the top row returns to the tab; Home/End; ↓ from a tab enters at the command focused last; a folded group opens with Enter, Space or ↓ into its popup, Escape returns to its button; focus survives a rescale.

## Acceptance criteria

- [ ] The open panel is one Tab stop; Tab from it leaves the ribbon
- [ ] Arrows move by on-screen position across groups, wrapping left and right; ↑ from the top row returns to the tab
- [ ] Folded groups open with Enter / Space / ↓ into their popup; arrows work inside; Escape returns to the button
- [ ] Focus stays on its command across a rescale; headless key tests cover each

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Keyboard only in the Gallery: Tab into the ribbon, cross every group with the arrows, open a folded group, Escape back to the tabs
