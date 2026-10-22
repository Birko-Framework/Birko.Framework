---
id: TASK-732
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P2
assignee: ai
created: 2026-10-22
depends-on: [TASK-731]
blocks: [TASK-736, TASK-740, TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: split buttons and dropdown menu buttons

## Context

FEATURE-019 D3 (approved by the user 2026-10-22): A command can open a menu of options (like Office's "Paste" with its small arrow): either the whole button opens the menu, or the main part runs the command and the arrow opens the menu.

RibbonItem is id/label/icon/href/action/badge/disabled/active/variant only, so a command cannot carry options.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [ ] RibbonItem can declare a menu (list of items) as a menu button or a split button
- [ ] The menu opens below the command in the top layer (not clipped by the panel), keyboard-operable, with Escape and light dismiss; choosing an item emits item-click with the item id and its parent
- [ ] Works at every group size and inside a folded group's flyout
- [ ] ARIA: aria-haspopup / aria-expanded, menu roles
- [ ] A Playground smoke suite covers it
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Open a split button by mouse and by keyboard, pick an option, check the main part still runs its own command
- [ ] Open one inside a folded group on a narrow window
