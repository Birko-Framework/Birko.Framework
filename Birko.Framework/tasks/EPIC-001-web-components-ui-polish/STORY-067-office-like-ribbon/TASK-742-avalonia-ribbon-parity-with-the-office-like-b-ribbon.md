---
id: TASK-742
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-22
depends-on: [TASK-730, TASK-731, TASK-732, TASK-733, TASK-734, TASK-735, TASK-736, TASK-737, TASK-738, TASK-739, TASK-740, TASK-741]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia Ribbon: parity with the Office-like b-ribbon (FEATURE-019)

## Context

FEATURE-019 D13 (approved by the user 2026-10-22): The desktop ribbon (Birko.Xaml.Avalonia) gets the same behaviours as the web one, after the web version.

Birko.Xaml.Avalonia has its own Ribbon (Ribbon.cs, RibbonGroupsPanel.cs, RibbonChunkButton.cs), which mirrors the web ribbon's scaling. The user chose web first, Avalonia later.

## Acceptance criteria

- [ ] Split once the web tasks settle: one Avalonia task per web behaviour that still applies
- [ ] Same layout rules (equal heights, aligned rows, labels below) and the same keyboard model

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] To be written per split task
