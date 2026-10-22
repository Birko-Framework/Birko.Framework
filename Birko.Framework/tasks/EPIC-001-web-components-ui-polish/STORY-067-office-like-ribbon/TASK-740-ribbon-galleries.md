---
id: TASK-740
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-22
depends-on: [TASK-732]
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: galleries

## Context

FEATURE-019 D11 (approved by the user 2026-10-22): A group can show a row of visual choices (like Office's style gallery) that scrolls and expands into a larger grid.

Not available; apps list the choices as plain commands.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [ ] An in-ribbon gallery of items with a preview, row scroll buttons and an expand button opening the full grid
- [ ] Scales down with the group (fewer visible items, then folded)
- [ ] Keyboard and ARIA as a listbox/grid
- [ ] A Playground smoke suite covers it
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Scroll, expand and pick from a gallery by mouse and keyboard at a wide and a narrow width
