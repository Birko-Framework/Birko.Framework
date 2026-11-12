---
id: TASK-922
parent: EPIC-001
feature: null
status: todo
priority: P2
assignee: null
created: 2026-11-12
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Escape in an open `b-time` panel neither collapses it nor returns focus to the trigger

Found while verifying TASK-921 (2026-11-12). Not caused by it.

## Context

The Playground's `picker-popup-smoke` (TASK-496) fails 2 of 67 in `verify.mjs`, both for `b-time`:

- `b-time: Escape in the panel collapses it`
- `b-time: ...and returns focus to the trigger`

These 2 checks failed in all three full runs on 2026-11-12: twice against the TASK-921 branch and once against
unmodified Birko.Web main 72d03cc. The same checks pass for `b-date-picker`, `b-date-range-picker` and
`b-datetime-picker`. In the same runs, `b-time`'s other checks pass: Enter expands it and moves focus into the panel.
So only Escape inside the panel is broken.

Not yet established: when it started failing and why. Candidates to check first, read from history and not measured:
- `dc59ddd` (v0.1.23, "listeners no longer pile up on re-render in ... b-time")
- TASK-819's Escape handling (v0.4.10 / v0.4.11, `b-select` / `b-multi-select`), in case a shared handler changed
- the test (`6438b1b`, TASK-550), in case the smoke itself became order-dependent

Bisect Birko.Web against the Playground smoke before choosing.

A user who opens a time picker and presses Escape is left with the panel open and focus inside it (keyboard / screen
reader users above all). In a `b-modal`, the Escape may instead reach the dialog. Check whether it does, as in TASK-819.

## Acceptance criteria

- [ ] Root cause found (bisected), recorded here.
- [ ] Escape in an open `b-time` panel collapses it and returns focus to the trigger, as in the other pickers.
- [ ] Inside a `b-modal`, that Escape closes only the panel, not the modal (TASK-819's rule).
- [ ] `picker-popup-smoke` 67/67, with the fix mutation-proven (reverting it fails the two checks).

## Human test plan

- [ ] Playground, `b-time` card: open the picker with the keyboard (Enter), press Escape. Expected: the panel closes
      and focus is back on the field. Repeat inside a modal: the modal stays open. With NVDA: the field announces
      "collapsed" again.
