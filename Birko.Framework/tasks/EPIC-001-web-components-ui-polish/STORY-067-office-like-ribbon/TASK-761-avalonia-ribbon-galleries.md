---
id: TASK-761
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

# Avalonia ribbon: galleries

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-740. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonItem.Gallery`: a row of visual choices with a full grid behind it, in the three looks the user kept (framed, open, dropdown); picking selects and reports; ▲ ▼ step a row; the grid works by keyboard; two tiles in a small group; the grid opens over a folded group's popup.

## Acceptance criteria

- [ ] The three looks, switchable; picking selects and reports; set / get the selection
- [ ] Row stepping with disabled ends; the grid by arrows, Enter, Escape; key tips
- [ ] Small and folded groups; the narrow menu lists the choices; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Scroll, expand and pick from the Styles gallery by mouse and keyboard, wide and narrow
