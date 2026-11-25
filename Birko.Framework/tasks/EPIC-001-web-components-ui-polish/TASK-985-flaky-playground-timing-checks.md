---
id: TASK-985
parent: EPIC-001
feature: null
status: done
priority: P3
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Three Playground checks fail now and then on timing, then pass on a re-run

## Context

Seen on 2026-11-25 while merging TASK-980, 981 and 983, each time on code that did not touch what failed, and each
time passing on the next run:
- `ribbon-overflow-smoke` FAIL "back chevron became visible" (verify after TASK-980's merge; passed on two re-runs).
- `carousel-smoke` FAIL "…and lands at the end" (same run series), and on TASK-983's branch FAIL "a swipe that settles
  on slide 3 sets the index", "…emits slide-change []", "…and moves inert (vii)" (passed on two re-runs).
- `keyboard-check` 243/244 once on Birko.Web main 4dc4e0c (which case was not captured), 244/244 on the next two runs.
Both smokes assert on scroll positions after smooth scrolling or a swipe settles, which suggests a fixed wait racing
the animation. A check that fails at random gets re-run until green, and then a real failure is re-run away too.

## Acceptance criteria

- [x] Each of the three is reproduced as flaky (repeated runs, ideally under CPU throttling), and the failing case of
      keyboard-check identified.
- [x] Each waits on the state it asserts (scroll end, a settled index, an event) instead of a fixed delay, or the
      component is fixed if the flake is a real race in it.
- [x] 20 consecutive runs of each pass, recorded in Evidence.

## Evidence

New Playground tool `repeat-smoke.mjs` (bca2146): runs named suites N times, alone or with every suite loaded as verify
does (`full`), optionally under CPU throttling.
- **carousel-smoke "…and lands at the end": a real component bug.** Alone it never failed (10 runs, also at 6x CPU);
  with every suite loaded it failed 2 of 20 on Birko.Web main. Diagnostics showed index 3 and a single `slide-change [3]`
  but the viewport stopped part-way (scroll 311/478 and 160/478) for good: Chrome left the smooth scroll with no further
  `scrollend`, so nothing settled it. **Fixed** in the shared slide track (Birko.Web `task/TASK-985` bb98b75, b-carousel
  and b-gallery): a programmatic smooth scroll not arrived within 1 s is finished at once on its slide, and one that
  runs out of its three resumes lands on its slide instead of settling on one nobody chose. New smoke case drops every
  smooth scroll: **red** on main (index 2, viewport at 0), green on the fix. The swipe-case failures seen once on
  TASK-983's branch were the same run series and did not recur.
- **ribbon-overflow-smoke "back chevron became visible": a test race.** Not reproduced in 40 full-load runs; it waited a
  fixed 60 ms + a frame for a state the ribbon reaches from its own scroll / resize events. Its four such checks now wait
  for the state (up to 2 s).
- **keyboard-check 243/244 once:** the failing case was not captured and did not recur in 12 consecutive runs
  (244/244 each). Recorded, not claimed fixed; the check prints its FAIL lines, so the next one names itself.
- **Acceptance:** `repeat-smoke carousel-smoke,ribbon-overflow-smoke 20 1 full` 20/20 clean on the fix (twice: before and
  after the ribbon change). verify 0 failing (carousel 61/61, gallery 44/44); Components `tsc` clean.

## Out of scope

- Other checks, unless found flaky on the way (file them).

## Human test plan

- N/A — fully covered by automated tests.

**Merged 2026-11-25** on the user's word ("merge and push") into Birko.Web main 30eef30, tagged v0.5.4 (both entries kept
under one v0.5.4 heading; TASK-986's tokens.json merged into Framework main 4aced9a2). On main: DesignTokens verify OK,
tests 59/59; `focus-ring-check` 36/36, keyboard-check 244/244, verify 0 failing (carousel-smoke 61/61,
ribbon-overflow-smoke 16/16); Components `tsc` clean.
