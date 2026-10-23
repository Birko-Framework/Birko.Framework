---
id: TASK-762
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

# Avalonia ribbon: input boxes and dropdowns

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-741. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonItem.Field`: text, number or dropdown (free values per dropdown), in the three caption looks the user kept; a committed value is reported once (Enter, a pick, leaving the box); arrows edit inside, Tab leaves, Escape reverts; folded and key-tip support.

## Acceptance criteria

- [ ] Text / number / dropdown boxes, free values per dropdown, the three caption looks
- [ ] Commit on Enter / pick / leaving, never twice; Escape reverts; arrows edit inside; Tab leaves
- [ ] Folded groups, key tips (focus with text selected), narrow menu; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Type a font name and a size not in the list, step Height with the arrows, Escape, and narrow the window
