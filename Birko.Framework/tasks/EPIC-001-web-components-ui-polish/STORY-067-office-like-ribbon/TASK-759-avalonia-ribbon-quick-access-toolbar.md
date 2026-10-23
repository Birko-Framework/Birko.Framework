---
id: TASK-759
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-752]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: Quick Access Toolbar (the app saves it)

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-737. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

Opt-in favourite commands before the tabs (or above / below): add and remove from a right-click, customise and reset from its ▾. Decided 2026-10-23: the app saves the choice; the ribbon exposes it and raises a change event, with no file I/O in the control.

## Acceptance criteria

- [ ] Opt-in toolbar with the app's defaults; right-click / Shift+F10 add and remove; ▾ customises and resets
- [ ] The choice is exposed (get / set) with a change event; the control writes no file
- [ ] One Tab stop with arrows; toggles kept in step; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Add and remove commands, reset, restart the Gallery: the app restores the toolbar
