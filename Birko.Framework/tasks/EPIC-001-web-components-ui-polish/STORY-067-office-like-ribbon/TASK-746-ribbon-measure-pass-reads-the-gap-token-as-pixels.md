---
id: TASK-746
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-22
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

- [x] The gap is resolved to pixels (any unit, `var()`, `calc()`), with the column gap as a fallback
- [x] A regression check: at widths just below what the old count allowed, no command is clipped by the panel
- [x] The `SEPARATOR_SLACK` comment no longer names the border, which TASK-730's follow-up removed

## Human test plan

- [x] Narrow the Playground ribbon slowly: at no width does a command get cut off at the right edge of the panel

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Reproduced.** Six groups swept from 1200 to 200px in steps of 5: at 19 widths the last group ran past the panel.
  Between 470 and 525px the overrun was up to 49px; between 200 and 230px, up to 32px.
- **Fixed at the cause.** The measure pass's probe takes `var(--b-ribbon-group-gap, …)` as its width and reads the
  pixels, so any unit, `var()` or `calc()` resolves. The column gap remains the fallback. `SEPARATOR_SLACK` is now
  `ROUNDING_SLACK`: the border it once covered is gone.
- **After.** Nothing is clipped between 235 and 1200px. Below about 230px six folded, compact groups still do not
  fit: there is no smaller variant, so that floor is unchanged and not claimed as fixed.
- **Decided by the user along the way.** Folding at the right width made a small group narrower than its name
  ("Records" over three icons), with all the extra space before the next divider. The user picked Office's centring
  from a live preview: a group's commands are centred over its name.
- **Gate.** Playground `ribbon-layout-smoke` 23/23.
  - The new sweep (460–540px, no group clipped) fails on the code before, by up to 48px.
  - The new centring check passes on the code before as well: before the fix no group got narrower than its name at
    the widths checked.
  - The divider check now measures group edges, since the extra space of a narrow group sits inside it.
  - `verify.mjs` clean twice; keyboard-check 195/195; entry-points 409/409; `tsc` clean.

## Sign-off (2026-10-22)

Signed off by the user in the Playground: spacing, the look in every theme, hover, key tips with Alt+Win and the
overall feel are good. The screen-reader checks were done through Chromium's accessibility tree instead of a live
screen reader, as the user asked (`ribbon-a11y-check.mjs`, 22/22; it found and fixed a menu shortcut announced
twice). Behaviour is covered by the automated suites: keyboard-check 195/195, `verify.mjs` clean.
