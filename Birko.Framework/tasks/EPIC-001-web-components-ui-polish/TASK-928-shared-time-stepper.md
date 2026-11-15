---
id: TASK-928
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-15
depends-on: [TASK-927]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-time and b-datetime-picker share one time row (− | 14 | + per number)

The user, 2026-11-15, after TASK-927's test: "why is the time picker in datetime looking differently than in b-time".

## Context

Two independent implementations. `b-time`: ▲ / ▼ above and below each number, named, `step` attribute.
`b-datetime-picker`: two plain number boxes with the browser's own tiny arrows (hover-only in Chrome), **no
accessible names**, minute step fixed at 5, and an untranslated "OK".

## Decision (the user's)

Six variants previewed in the Playground with the real tokens (`wwwroot/time-variants.html`, a throwaway page):
A today's b-time, B small arrows beside, C − / + beside, D ‹ › beside, E one joined − | 14 | + control, F today's
datetime. The user asked for arrows beside the box and picked **E: "since birko.web is used in touch devices too"**.

## Acceptance criteria

- [x] One shared producer for the row (`inputs/time-stepper.ts` + `timeStepperSheet`), used by both components.
- [x] Full-height buttons; on `pointer: coarse` they grow to `--b-control-min-height-lg` and the box keeps the iOS
      16px floor.
- [x] Every button and number box named (`bwc.time.*`); datetime's OK translated (`bwc.dialog.ok`).
- [x] `b-datetime-picker` takes `step` (default 5, as before).
- [x] Focus stays on the pressed button across the panel rebuild; typing, wheel, ↑ / ↓ still work.
- [x] README, CHANGELOG (inside the unpushed v0.4.12); `--b-time-picker-width` 11rem → 13.5rem.

## Human test plan

- [x] Playground: b-time and b-datetime-picker cards look the same in the time row, light and dark; − / + change the
      numbers, the panel stays open; type a number; Tab through − 14 + : − 35 +. On a phone or a touch screen the
      buttons are easy to hit.

## Built (2026-11-15)

Birko.Web `task/TASK-928` (worktree `Web.TASK-928`); Playground `task/TASK-928`. picker-popup-smoke 88/88 (12 new
checks), verify.mjs 0 failing (one run had a carousel drag flake, passed on rerun), keyboard-check 225/225,
a11y-name-check OK, a11y-description-check PASS, Components `tsc` clean. Real-mouse probe: all popups in place.
Screenshots light / dark / touch checked. 

## Closed (2026-11-15)

Human test: the user looked at both pickers in the Playground ("they look good"). They had no mouse for the wheel, so
it was checked with real wheel input (CDP) on the branch build: hour and minute, up then down, in both components;
the step followed (1 in b-time, 5 in datetime), the panel stayed open and the page did not scroll.
Merged: Birko.Web main 677e238, inside the unpushed **v0.4.12** (tag moved); Playground main f6a1b35. Not pushed.
