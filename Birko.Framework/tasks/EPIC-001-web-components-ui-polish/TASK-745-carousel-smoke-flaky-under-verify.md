---
id: TASK-745
parent: EPIC-001
status: todo
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

- [ ] The cause is measured: a timing assumption in the suite, or a real b-carousel defect under load
- [ ] Fixed at the cause, with ten clean `verify.mjs` runs in a row

## Human test plan

N/A — fully covered by automated tests
