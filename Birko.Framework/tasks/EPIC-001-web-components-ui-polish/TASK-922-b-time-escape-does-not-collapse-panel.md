---
id: TASK-922
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
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

- [x] Root cause found, recorded here (by reading the handlers and a deterministic reproduction, not a bisect; see below).
- [x] Escape in an open `b-time` panel collapses it and returns focus to the trigger, as in the other pickers.
- [ ] Inside a `b-modal`, that Escape closes only the panel, not the modal (TASK-819's rule).
- [x] `picker-popup-smoke` 67/67, with the fix mutation-proven (reverting it fails the two checks).

## Human test plan

- [x] Playground, `b-time` card: open the picker with the keyboard (Enter), press Escape. Expected: the panel closes
      and focus is back on the field. Repeat inside a modal: the modal stays open. With NVDA: the field announces
      "collapsed" again.

## Root cause and fix (2026-11-15), at the user's word ("also then the b-time")

Not b-time. TASK-819's follow-up (v0.4.11, Birko.Web 515490d) made an open `b-multi-select` list take Escape on the
**document in the capture phase**, `preventDefault` + `stopPropagation`, whatever the Escape was aimed at. While any
list was open anywhere on the page, no other control received Escape. The Playground runs every smoke suite in one
page, so whenever another suite had a multi-select list open at that moment, the picker's Escape was swallowed. That
explains the intermittency: today's runs failed on b-time in some, on `b-date-range-picker` in one, and passed in others
(TASK-923/924/926 runs had 0 failing checks). A user hits it by tabbing from an open multi-select list to a picker:
Escape then closes the list, not the picker, and focus stays inside the picker panel.

Fix: the list takes only an Escape whose path includes its own element, or one that starts nowhere (`body`,
`document`, or a `<dialog>`: TASK-819's case, where focus fell when a pick rebuilt the options).

Proof: new deterministic check in `picker-popup-smoke` (open a multi-select list, open b-time with the keyboard,
Escape in its panel). Against unfixed code 3 checks are red (b-time stays open, focus not returned, the list is closed
instead); fixed, green. Unmodified, the suite failed 4 Escape checks in the baseline run. Fixed: picker-popup-smoke
72/72 in three consecutive full runs, verify.mjs failing checks 0 each time; TASK-819's real-mouse
`escape-in-modal-check.mjs` 27/27; Components `tsc` clean.

Not separately tested: b-time inside a `b-modal`. b-time's panel handler already prevents and stops Escape (unchanged),
so the modal does not receive it; the human test plan covers it.

Birko.Web main 96134ad, tagged **v0.4.17** locally; Playground main 8118d1a. Not pushed. Status `verify` until the
human test plan runs.

Release consolidated (2026-11-15, the user's call): the unpushed Birko.Web tags v0.4.12-v0.4.17 were folded into one
**v0.4.12** (Birko.Web 635340e), which carries this change. Any v0.4.13-v0.4.17 above refers to a tag that no longer exists.

## Closed (2026-11-15)

Human test passed: the user, in the Playground on the merged main ("all works"). Ships in Birko.Web v0.4.12, not yet
pushed.
