---
id: TASK-755
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

# Avalonia ribbon: split buttons and menu buttons

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-732. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonItem.Menu` and `Split`: a menu button opens a menu; a split button runs its command, and its ▾ part opens the menu (Office's "Paste ▾"); choices with icon, label, shortcut and separators; Alt+↓ opens; a choice reports the choice with its parent; inside a folded group the menu opens from the popup.

## Acceptance criteria

- [ ] Menu and split buttons with the picked look (divider on hover); choices with icon, label, shortcut, separators, disabled
- [ ] Enter / Space / Alt+↓ open at the first choice; arrows wrap; Escape returns to the button
- [ ] A choice reports its id and its command; works inside a folded group and in the narrow menu; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Open Paste ▾ and CSV by mouse and keyboard, pick an option, check Paste still pastes; repeat inside a folded group
