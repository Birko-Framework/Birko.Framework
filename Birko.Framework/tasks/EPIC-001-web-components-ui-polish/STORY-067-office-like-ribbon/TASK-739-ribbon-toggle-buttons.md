---
id: TASK-739
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: toggle buttons with a pressed state

## Context

FEATURE-019 D10 (approved by the user 2026-10-22): A command can be on or off (like Bold), and shows its state.

RibbonItem.active highlights an item but is not exposed as a pressed state and does not toggle itself.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [ ] RibbonItem can be a toggle: aria-pressed, a pressed look from tokens, click flips it and emits item-click with the new state
- [ ] The app can set the state (selection changed) without an event loop
- [ ] Toggle groups where only one can be on (alignment) are supported or explicitly left out
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Toggle Bold/Italic by mouse and keyboard; check the pressed look in every theme and the screen-reader state
