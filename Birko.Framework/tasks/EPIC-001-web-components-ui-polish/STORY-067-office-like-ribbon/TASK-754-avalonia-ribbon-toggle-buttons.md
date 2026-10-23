---
id: TASK-754
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

# Avalonia ribbon: toggle buttons and one-of sets

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-739. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonItem.Toggle` / `Pressed` / `ToggleGroup`; a click flips a toggle (or picks a set member and releases the others); the pressed look is the tinted and outlined one the user picked; `SetPressed` / `IsPressed`; state kept across rescales and in the narrow menu; automation toggle state.

## Acceptance criteria

- [ ] Toggle and one-of sets with the picked pressed look, no layout shift
- [ ] `SetPressed()` silent, `IsPressed()`; the click reports the new state
- [ ] Kept across rescales and in the narrow menu; automation toggle state; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Toggle Bold and Italic, switch Columns / Rows by mouse and keyboard; check the pressed look in every theme
