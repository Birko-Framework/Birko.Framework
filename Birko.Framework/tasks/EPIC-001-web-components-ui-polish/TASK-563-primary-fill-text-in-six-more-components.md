---
id: TASK-563
parent: EPIC-001
feature: null
status: verify
priority: P2
assignee: ai
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

- [x] All six use `var(--b-text-on-primary, var(--b-text-inverse))`
- [x] A fresh sweep (`background: var(--b-color-primary)` paired with `--b-text-inverse`) finds no other
      site, or each remaining one carries a comment saying why
- [x] Hover or selected-hover states that sit on `--b-color-primary-hover` are checked too

## Out of scope

- Primary as a text colour (45 rules). They pass on dark surfaces and are untouched

## Human test plan

- [ ] In dark: a selected day in each date picker, the active markdown-editor mode, a selected option and a
      tour's primary button all read as dark text on blue

## Progress log

- 2026-10-11 — implemented in Birko.Web `dd56ede`. The fresh sweep found **four more** sites than the Context listed: `b-date-range-picker`'s range-end hover and its `.drp-actions button.primary`, and `b-chat`'s user bubble and `.send-btn`. Twelve rules in all now use `var(--b-text-on-primary, var(--b-text-inverse))`. Remaining `--b-text-inverse` text sits on success/danger/warning/info fills (`b-toast`, `b-button .danger`, `b-ribbon`), which is not this token's job. Every hover/selected-hover lands on `--b-color-primary-hover` (dark: `#0f172a` on `#60a5fa` 7.02:1; light: white on `#1d4ed8` 6.70:1); `hover-end` is a dashed outline with no fill, so its text keeps the default colour
- 2026-10-11 — `tsc --noEmit` on `Birko.Web.Components` and Presenter's typecheck: clean. **Human test plan not yet run** — Presenter renders none of these components, so sign-off needs an app that does (status `verify`)
