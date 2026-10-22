---
id: TASK-738
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

# b-ribbon: rich tooltips (title, description, shortcut)

## Context

FEATURE-019 D9 (approved by the user 2026-10-22): Hovering a command shows a proper tooltip with the command name, a one-line description and its keyboard shortcut, instead of the browser's plain label.

Commands use the native title attribute (label only).

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [ ] RibbonItem can carry a description and a shortcut; the tooltip shows title, description and shortcut
- [ ] Built on b-tooltip (or its pattern): delay, keyboard focus shows it, Escape hides it, aria-describedby links it
- [ ] No native title left behind to double-announce
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Hover and keyboard-focus commands: the tooltip shows, reads in a screen reader, and hides on Escape
