---
id: TASK-985
parent: EPIC-001
feature: null
status: todo
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

- [ ] Each of the three is reproduced as flaky (repeated runs, ideally under CPU throttling), and the failing case of
      keyboard-check identified.
- [ ] Each waits on the state it asserts (scroll end, a settled index, an event) instead of a fixed delay, or the
      component is fixed if the flake is a real race in it.
- [ ] 20 consecutive runs of each pass, recorded in Evidence.

## Out of scope

- Other checks, unless found flaky on the way (file them).

## Human test plan

- N/A — fully covered by automated tests.
