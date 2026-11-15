---
id: TASK-931
parent: null
feature: null
status: todo
priority: P2
assignee: agent
created: 2026-11-15
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `Task721XmlStoreSafetyTests.A_failed_async_batch_save_loses_no_batch` failed once on CI (Linux)

## Context

build-and-test run 43700238553 (Birko.Framework db86c349, 2026-11-15) failed on this one test, 1 of 502 in
Birko.Data.XML.Tests. That commit touched only DesignTokens' `tokens.json` and the Avalonia theme files; the run
before it (0bed1336, the same XML code) passed, and it passes 8 of 8 runs locally on Windows. The CI log shows only
`[FAIL]`, no assertion message (quiet verbosity). The other red build-and-test runs of the last weeks (Nov 4–5) were a
different test (`TestTempDirectoryTests`). The failed job was re-run on 2026-11-15 on the same commit and **passed**: intermittent, not deterministic.

**Hypothesis, unverified (rule 54):** the test injects the failure on the SECOND write (`flaky.FailOnWrite(2)`). If the
async batch store writes its batch files concurrently, which file is "the second write" depends on scheduling, so the
injected failure can land on a different batch than the test assumes, on Linux more than on Windows. Reproduce first:
loop the test on Linux (an sdk:10.0 container, as TASK-919 did) and read the store's write order.

## Acceptance criteria

- [ ] Reproduced on Linux (or recorded as not reproducing after N runs, rule 54), with the assertion that failed.
- [ ] Root cause in the test or in the store. If the store can lose a batch when writes run concurrently, that is
      a product defect (TASK-721's guarantee), not a test fix.
- [ ] The test fails for the right reason only: deterministic fault injection.

## Human test plan

N/A — fully covered by automated tests.
