---
id: TASK-741
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-22
depends-on: [TASK-731]
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: inputs and comboboxes inside the ribbon

## Context

FEATURE-019 D12 (approved by the user 2026-10-22): A group can hold a small input or dropdown (like Office's font name and size boxes).

Groups hold buttons only.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [ ] RibbonItem can be a text input, a number input or a select (reusing b-input / b-select bare, size sm)
- [ ] Its value is reported with an event; arrow keys inside it edit, Tab or Escape leave it
- [ ] Scales and folds with its group
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Type into a ribbon input and pick from a ribbon dropdown by keyboard; narrow the window and check it folds
