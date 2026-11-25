---
id: TASK-986
parent: EPIC-001
feature: null
status: verify
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

- [x] `--b-focus-ring-inset` (and `--b-focus-ring-danger-inset`, so the pair stays symmetric) in `tokens.json` for every
      theme, as an all-inset form of the same ring: the line and the glow both inside, same colours as TASK-983's.
- [x] The line still has 3:1 against the control's own fill in every theme, measured.
- [x] Generated; DesignTokens `verify` and tests pass.
- [x] Components README names the inset tokens next to `--b-focus-ring`; CHANGELOG entry.
- [x] `focus-ring-check` extended: both inset tokens resolve to inset-only shadows in every theme with a 3:1 line.

## Evidence

Each theme's ring with `inset` on both shadows: e.g. base `inset 0 0 0 1px #2563eb, inset 0 0 4px 1px rgba(37, 99, 235,
0.5)`; same colours as TASK-983 in base, dark, neon, finstat and the inverse area. Framework `task/TASK-986` fc9a67ba
(`tokens.json`; the AXAML only updates its "N tokens deferred" count); generated into Birko.Web `task/TASK-986` 8bc8eb4,
which starts v0.5.4 (README design-token table names both forms and why not `inset var(--b-focus-ring)`).
- Playground 931b1f1 `focus-ring-check` 36/36: in every theme and both inverse areas, both inset tokens resolve to
  all-inset shadows whose line is 3.64–15.53:1 against the page and the secondary surface. **Red** 24/36 on Birko.Web
  main (the tokens do not exist).
- DesignTokens `verify` OK, tests 59/59 against the two branches; verify 0 failing; theme-roundtrip 11/11.

## Out of scope

- Changing any Birko.Web component to use it (none draws its ring inside).

## Human test plan

- [ ] In DraCode, with `.phone-tab` switched to the token: Tab through the phone tab bar in light and dark; the ring and
      its glow stay inside each tab.
