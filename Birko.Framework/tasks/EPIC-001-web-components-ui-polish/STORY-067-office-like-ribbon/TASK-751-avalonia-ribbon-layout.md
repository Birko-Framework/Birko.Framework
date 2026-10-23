---
id: TASK-751
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

# Avalonia ribbon: equal-height groups, aligned rows, labels below, dividers halfway

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-730, TASK-743, TASK-746. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

Groups one height with the divider running the full height; three equal command rows lined up across groups; each group's name centred under it; the divider halfway between groups, spaced by the group-gap token (16px); a group narrower than its name centres its commands; a compact folded group without an icon shows its initial.

## Acceptance criteria

- [ ] Every group in a row is the same height; rows of commands line up across groups
- [ ] Group names sit centred under their group
- [ ] The divider is halfway between two groups, from a gap resource (16px default), and adds no width the scaling cannot see
- [ ] A group narrower than its name centres its commands; a compact folded group without an icon shows its initial
- [ ] Headless tests measure each of these

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Open the Gallery ribbon in light and dark: dividers full height and halfway, rows aligned, names below; narrow the window until groups fold, with and without group icons
