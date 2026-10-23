---
id: TASK-756
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

# Avalonia ribbon: group dialog launcher

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-733. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonGroup.Launcher` / `LauncherLabel`: Office's small corner arrow at the group's bottom right, beside its name; a click, Enter or its key tip raises a launcher event; named and tooltipped; in a folded group it moves into the popup's header.

## Acceptance criteria

- [ ] Corner arrow beside the group name, the name staying centred
- [ ] Click / Enter raise the launcher event, not a command click
- [ ] Named and tooltipped; in the popup header when folded; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Click and keyboard-activate the Font launcher in the Gallery, wide and folded
