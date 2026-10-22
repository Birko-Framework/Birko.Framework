---
id: TASK-746
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: the measure pass reads the gap token as pixels

## Context

Found during the TASK-730 divider follow-up. `_measureAndScale` charges each gap between groups as
`parseFloat(getComputedStyle(this).getPropertyValue('--b-ribbon-group-gap')) + SEPARATOR_SLACK`. The token is in
rem, so `1.5rem` counted as 1.5px, not 21px (at the Playground's 14px base) or 24px. Since the follow-up it is
`var(--b-space-lg)`, which reads as `1rem` and counts as 1px.

The policy therefore believes the row is narrower than it is, by about (gap − 1) × (groups − 1). It keeps groups at a
larger size than fits, and the panel is `overflow: hidden`, so the surplus is clipped. The commands at the end of
the row then cannot be reached, which is what STORY-049 forbids.

The suites passed because a group that does not fit is not something they measure at every width.

## Acceptance criteria

- [ ] The gap is resolved to pixels (any unit, `var()`, `calc()`), with the column gap as a fallback
- [ ] A regression check: at widths just below what the old count allowed, no command is clipped by the panel
- [ ] The `SEPARATOR_SLACK` comment no longer names the border, which TASK-730's follow-up removed

## Human test plan

- [ ] Narrow the Playground ribbon slowly: at no width does a command get cut off at the right edge of the panel
