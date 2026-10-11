---
id: TASK-563
parent: EPIC-001
feature: null
status: todo
priority: P2
assignee: null
created: 2026-10-11
depends-on: [TASK-562]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Six more components put white text on a primary fill — below AA in dark

## Context

Spawned from TASK-562, which added `--b-text-on-primary` and moved `b-button` `.primary` and
`b-pagination` `.page-btn-active` onto it. The same pattern, `background: var(--b-color-primary)` with
`color: var(--b-text-inverse)`, is in six more places. In dark that is white on `#3b82f6`, 3.68:1:

- `inputs/b-date-picker.ts:163`, `inputs/b-date-range-picker.ts:262`, `inputs/b-datetime-picker.ts:160`:
  the selected day
- `inputs/b-markdown-editor.ts:121`: `.mode-btn.active`
- `inputs/b-option-group.ts:66`: the selected option
- `layout/b-tour.ts:137`: `button.primary`

They were left out of TASK-562 because its acceptance named the two controls Presenter's audit hit.

## Acceptance criteria

- [ ] All six use `var(--b-text-on-primary, var(--b-text-inverse))`
- [ ] A fresh sweep (`background: var(--b-color-primary)` paired with `--b-text-inverse`) finds no other
      site, or each remaining one carries a comment saying why
- [ ] Hover or selected-hover states that sit on `--b-color-primary-hover` are checked too

## Out of scope

- Primary as a text colour (45 rules). They pass on dark surfaces and are untouched

## Human test plan

- [ ] In dark: a selected day in each date picker, the active markdown-editor mode, a selected option and a
      tour's primary button all read as dark text on blue
