---
id: TASK-737
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: Quick Access Toolbar

## Context

FEATURE-019 D8 (approved by the user 2026-10-22): A small row of favourite commands always visible above or below the tabs, which the user can add to and remove from, and which is remembered.

No such row exists.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [ ] The app declares the toolbar's default commands; the user can add a command (context menu "Add to Quick Access Toolbar") and remove one
- [ ] The choice persists under a key (localStorage, as split-panel sizes do), hostile stored values ignored
- [ ] Default position in the tab row, before the tabs (decided by the user from a live preview, Q3); an attribute
      moves it to its own row above the tabs or below the ribbon; keyboard-reachable; announced
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Add and remove commands, reload: the toolbar keeps them; check it at a narrow width
