---
id: TASK-734
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

# b-ribbon: contextual tabs

## Context

FEATURE-019 D5 (approved by the user 2026-10-22): Some tabs appear only while something is selected (like Office's "Table Design" when a table is selected), shown together under a coloured heading, and disappear again when the selection goes.

RibbonTab.category only draws a divider between categories; tabs cannot be shown or hidden as a set or coloured.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [ ] A set of tabs can be declared contextual with a name and a colour, and shown or hidden by the app at runtime
- [ ] Shown sets sit after the normal tabs with their heading and colour; hiding the set while one of its tabs is active falls back to the previous normal tab and emits tab-change
- [ ] Colours come from tokens and pass contrast in every theme
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Select and deselect something in a demo page: the contextual tabs appear and disappear, the active tab falls back
- [ ] Check the heading colour in light, dark, neon, finstat and inverse
