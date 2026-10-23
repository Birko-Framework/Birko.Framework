---
id: TASK-763
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-751, TASK-752, TASK-753, TASK-754, TASK-755, TASK-756, TASK-758, TASK-759, TASK-760, TASK-761, TASK-762]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: every size a theme resource

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-747. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

The web ribbon's sizes are all `--b-*` tokens (TASK-747). On the desktop, every size the ribbon draws comes from a theme resource in `Themes/Tokens.axaml`, with a check that fails on a literal.

## Acceptance criteria

- [ ] Every size the ribbon draws is a theme resource; no visible change, measured
- [ ] A test fails on a literal size in the ribbon code

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

N/A — fully covered by automated tests
