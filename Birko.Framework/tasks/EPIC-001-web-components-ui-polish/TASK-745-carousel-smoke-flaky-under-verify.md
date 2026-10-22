---
id: TASK-745
parent: EPIC-001
status: done
priority: P3
assignee: ai
created: 2026-10-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Playground carousel-smoke fails about one verify run in five

## Context

Found while closing TASK-741. Its suites do not touch b-carousel. `node verify.mjs` in Birko.Web.Playground failed
in 2 of 10 runs, both times in `carousel-smoke`:

- `…the index is clamped to the new last page (0)`
- `…and lands at the end`

`verify.mjs` runs every in-page smoke suite at once, so another suite's load or focus may be the trigger, as it was
for `ribbon-scaling-smoke` (TASK-740, TASK-741). The four runs earlier the same day were clean. TASK-741 added inputs
to the Playground ribbon demo, which makes the page slightly heavier.

## Acceptance criteria

- [x] The cause is measured: a timing assumption in the suite, or a real b-carousel defect under load
- [x] Fixed at the cause, with ten clean `verify.mjs` runs in a row

## Human test plan

N/A — fully covered by automated tests

## Outcome (2026-10-22)

A real `SlideTrack` defect, shared by b-carousel and b-gallery, that the Playground's load exposed.

- **Measured, not assumed.** A new `?smoke=1&only=…` filter in the Playground runs chosen suites alone.
  - carousel-smoke alone: 0 failures in 20 runs.
  - With every suite: failures on three different checks.
  - Bisected to two groups: the grid benchmark plus the table suites, and the locale switches (which re-render every
    component). Both stall the main thread. Modal, ribbon and other suites: 0 failures in 15 runs each.
- **Cause 1.** The 150ms `scroll` debounce settled mid-scroll whenever the main thread stalled that long. It is now only
  the fallback for engines without `scrollend`. Bad groups after: 0 failures in 40 runs (3 in 30 before).
- **Cause 2, found by instrumenting `slide-change`** (scroll position and stack at each one). Chrome ended a stalled
  smooth scroll early: `scrollend` at the start, a settle on slide 0, then the scroll carried on to the target and
  settled again. The index went 4 → 0 → 4, with a spurious `slide-change` and announcement.
  - `scrollTo()` now records its target. A settle short of it, with no user input since, re-issues the scroll (at
    most 3 times).
  - Pointer, wheel, touch or key input on the viewport clears the target.
  - All suites after: 0 failures in 40 runs (4 in 40 with cause 1 fixed alone).
- **Regression tests** in Playground `carousel-smoke`:
  - a 250ms main-thread stall in the middle of a smooth scroll must settle once, on the chosen slide. It fails 5/5
    on the code before;
  - a user takeover during a programmatic scroll settles where the user left it.
- **Gate.** `verify.mjs` clean ten runs in a row; entry-points 409/409; `tsc` clean.

