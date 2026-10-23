---
id: TASK-752
parent: STORY-067
feature: FEATURE-019
status: verify  # verified headlessly 2026-10-23; the human test plan is left
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

- [x] The open panel is one Tab stop; Tab from it leaves the ribbon
- [x] Arrows move by on-screen position across groups, wrapping left and right; ↑ from the top row returns to the tab
- [x] Folded groups open with Enter / Space / ↓ into their popup; arrows work inside; Escape returns to the button
- [x] Focus stays on its command across a rescale; headless key tests cover each

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Keyboard only in the Gallery: Tab into the ribbon, cross every group with the arrows, open a folded group, Escape back to the tabs

## Outcome (2026-10-23)

- **Before.** Every visible command was its own Tab stop. Left/Right cycled through the commands in tree order, so
  Right from a command went to the one below it. ↓ from a tab always entered at the first command. A rescale disabled
  the focused (now parked) command, and focus dropped out of the ribbon.
- **Now.**
  - Roving `IsTabStop` on one stop.
  - Spatial arrows via `NearestInDirection` (a port of b-ribbon's), wrapping Left/Right; ↑ from the top row returns
    to the active tab; Home/End.
  - ↓ from a tab enters at the stop focused last; ↓ on a folded group opens its popup with focus on its first command.
  - Inside a popup the arrows are spatial too, and Left/Right keep the wrap the desktop already had.
  - Each stop has a stable key in its `Tag`. When `RibbonGroupsPanel` parks the focused control it raises
    `FocusDisplaced`, and the ribbon refocuses the same command at its new size, or its group's button.
- **Changed tests, on purpose (D2).**
  - `Arrow_keys_move_between_commands_in_the_groups_row`: Right from Clipboard's first command now lands on Records'
    first command.
  - `Tab_walks_through_the_ribbon_…`: Tab reaches the panel once.
- **Gate.** New `RibbonKeyboardTests`, 5 tests, all red on the code before:
  - one Tab stop that remembers the last command, and Tab leaves the panel;
  - arrows by position with wrapping, Up/Down in the column, Home/End;
  - ↑ to the tab and ↓ back to the last command; ↓ opens a folded group;
  - focus kept through a rescale from 1000 to 220px.
  - Birko.Xaml.Avalonia.Tests 223/223.

