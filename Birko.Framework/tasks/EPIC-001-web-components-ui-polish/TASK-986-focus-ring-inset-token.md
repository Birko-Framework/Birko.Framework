---
id: TASK-986
parent: EPIC-001
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: [TASK-983]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A `--b-focus-ring-inset` token for rings drawn inside the control

## Context

Asked for by DraCode (2026-11-25; its user chose a token over a hand-written shadow). Since TASK-983, `--b-focus-ring`
is two shadows, a 1px line and a tight glow. A consumer that draws the ring inside a control with
`box-shadow: inset var(--b-focus-ring)` (DraCode's phone tab bar, `app-shell.ts:177`) now gets `inset` on the line only:
the glow lands outside, spilling onto neighbouring tabs or cut off by the bar. Birko.Web itself has no inset use.

## Acceptance criteria

- [ ] `--b-focus-ring-inset` (and `--b-focus-ring-danger-inset`, so the pair stays symmetric) in `tokens.json` for every
      theme, as an all-inset form of the same ring: the line and the glow both inside, same colours as TASK-983's.
- [ ] The line still has 3:1 against the control's own fill in every theme, measured.
- [ ] Generated; DesignTokens `verify` and tests pass.
- [ ] Components README names the inset tokens next to `--b-focus-ring`; CHANGELOG entry.
- [ ] `focus-ring-check` extended: both inset tokens resolve to inset-only shadows in every theme with a 3:1 line.

## Out of scope

- Changing any Birko.Web component to use it (none draws its ring inside).

## Human test plan

- [ ] In DraCode, with `.phone-tab` switched to the token: Tab through the phone tab bar in light and dark; the ring and
      its glow stay inside each tab.
