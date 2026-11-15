---
id: TASK-927
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-15
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-time / b-datetime-picker panel jumps away (and then closes) while you use it

Found by the user in the Playground, 2026-11-15, while testing TASK-922: "the btime card should not hide every time i
click the arrows".

## Context

Measured with a real mouse (puppeteer) on the Playground: after one `b-time` arrow click the panel was still open but
had moved from y 531 to y 2232 (off screen); the second click landed where the arrow had been, outside the panel, and
the outside-click handler closed it. Cause: an arrow sets the `value` attribute (observed), the component re-renders,
and the render drops the fixed panel's inline `top` / `left`, which were set only when it opened.

The user asked whether other components have the same pattern. The same real-mouse probe over every popup component
in the Playground: **`b-datetime-picker`** has it (picking a date keeps its panel open for the time, re-renders: y 531 →
1841). `b-date-picker` (month nav), `b-date-range-picker` (nav, first date), `b-multi-select` (option clicks) and
searchable `b-select` (typing) keep their place.

## Acceptance criteria

- [x] A `b-time` arrow click keeps the panel open and in place; repeated clicks too.
- [x] A `b-datetime-picker` date pick keeps the panel open and in place.
- [x] The other popup components checked (above).
- [x] Regression checks, red before.

## Human test plan

- [x] Playground (http://localhost:5180/), b-time card: open it, click the hour and minute arrows several times: the
      panel stays where it opened and the time changes. b-datetime-picker card: open it, pick a date: the panel stays
      in place for the time; Set closes it.

## Fixed (2026-11-15)

Each component re-places its panel through one `_positionPanel` (used on open and, while open, after every render).
`picker-popup-smoke`: the b-time checks fail on unfixed code (top 72 → 14); the datetime check fails with its one line
removed (72 → 14); fixed, 76/76 with 0 failing checks in two full runs. Real-mouse probe after the fix: all six popup
components stay in place. Components `tsc` clean.
Released inside the unpushed **v0.4.12** (the user's one-tag release): Birko.Web main cda982b (tag moved there);
Playground main 56509c6. Not pushed.

## Closed (2026-11-15)

Human test passed: the user, in the Playground ("all works"). Ships in Birko.Web v0.4.12, not yet pushed.
