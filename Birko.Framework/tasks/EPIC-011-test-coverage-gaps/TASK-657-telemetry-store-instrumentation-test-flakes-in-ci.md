---
id: TASK-657
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-17
depends-on: []
blocks: []
findings: [FIELD-048]
pr: null
github-issue: null
jira-key: null
---

# `StoreInstrumentationTests.ExecuteAsync_OnException_RecordsErrorAndRethrows` flaked in CI

## Context

**Observed once, not reproduced.** `build-and-test` run 39054139288 (attempt 1, commit `1d1eb8ef`, 2026-10-17) failed
on `Birko.Telemetry.Tests.StoreInstrumentationTests.ExecuteAsync_OnException_RecordsErrorAndRethrows`: 1 of 61 failed.
The commit touched nothing the suite imports. Re-running the failed job on the same commit passed (attempt 2), and 40
consecutive local runs of the suite passed. The trx with the failure message is in that run's artifacts.

**Hypothesis (rule 54: recorded, not adopted).** `StoreInstrumentationTests`, `InstrumentedStoreWrapperTests` and
`AsyncInstrumentedStoreWrapperTests` each start a `MeterListener` on the same process-wide meter
(`BirkoTelemetryConventions.MeterName`), and xUnit runs the three classes in parallel. Every listener receives every
measurement, and the callbacks append to a plain `List<T>` (`tests/Birko.Telemetry.Tests/StoreInstrumentationTests.cs:28-35`).
So measurements from a sibling class call this listener from other threads, and a concurrent `List.Add` can lose an
entry, including the error counter the test asserts. This is the shape of rule 61: a parallel sibling damaged through
process-wide state, the victim not the file that caused it.

## Acceptance criteria

- [ ] Reproduced (for example, a stress loop running the three classes in parallel with extra measurements), or the
      hypothesis falsified and another cause found
- [ ] The listeners record thread-safely, and/or the three classes share an xUnit collection; each test asserts only
      measurements it caused (tag or entity name), so a sibling's measurements cannot satisfy it either
- [ ] The suite passes a stress run that failed before

## Out of scope

- Instrumentation behaviour itself: production code is not implicated by anything measured.

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-657` — leave empty until then._
