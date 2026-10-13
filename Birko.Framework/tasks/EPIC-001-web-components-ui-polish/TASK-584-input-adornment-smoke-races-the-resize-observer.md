---
id: TASK-584
parent: EPIC-001
feature: FEATURE-001
priority: P3
status: done
assignee: ai
created: 2026-10-13
depends-on: []
blocks: [TASK-582]
findings: [FIELD-030]
pr: null
github-issue: null
jira-key: null
---

# `input-adornment-smoke` sleeps 60 ms and hopes `b-input`'s ResizeObserver has fired

## Context

Found while doing TASK-582. `b-input` applies the padding that clears a prefix, suffix or × from a
`ResizeObserver` callback (`Birko.Web.Components/src/inputs/b-input.ts:210`, `watchAdornments`), on purpose: a
synchronous measurement locked up the grid benchmark. The Playground suite `src/input-adornment-smoke.ts` mounts a
field and then waits a **fixed** `settle()` of 60 ms (line 9) before measuring. If the observer fires later than that,
the suite measures the field without its padding and fails.

**Measured 2026-10-13:** TASK-582's change to `BasePage`'s header markup (a wrapper `div`, rendered only in pages
other suites mount briefly) made `md: text starts after the prefix (text from 25.5, prefix ends 66.5)` fail on
**4 of 4** runs, against **3 of 3** passes without it. Bisected to the wrapper alone. On the failing build, waiting
for the padding instead of sleeping made the suite pass 50/50, so `b-input` is correct and the suite is racing.
`sm: no layout shift as the × appears` failed the same way once in another variant. Any unrelated change to what
runs alongside can flip these checks.

Checks that depend on the observer: the prefix mount (start padding), the clearable + suffix mount (end padding,
captured as `emptyPad` before the × appears), and the slotchange case (padding grows, then returns).

## Acceptance criteria

- [x] Every check that depends on the observer waits for the padding it writes (the input's inline
      `padding-inline-start` / `-end`), bounded, instead of a fixed sleep
- [x] A wait that times out fails visibly, as a named precondition check, rather than measuring the wrong state
- [x] The suite passes on the TASK-582 build that failed 4 of 4, on at least two runs
- [x] It still fails when `b-input` really does not apply the padding (proved once, e.g. with
      `applyAdornmentPadding` made a no-op)

## Out of scope

- Other suites' fixed sleeps. Only this suite was measured racing.
- `b-input` itself: it was measured correct.

## Human test plan

N/A — a test-harness fix, verified by the Playground runs in the acceptance criteria.

## Progress log

- 2026-10-13 — Spawned from TASK-582 and fixed first, because it held that task's verify red. Playground `ff71a5a`:
  three observer-dependent mounts (prefix, clearable + suffix, slotchange grow and shrink) wait for the inline padding,
  up to 2 s, with a named precondition check. 58/58 on two runs of the TASK-582 build (full verify exit 0 both
  times); 44/58 with `applyAdornmentPadding` made a no-op, every precondition named.
