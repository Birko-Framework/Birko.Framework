---
id: TASK-657
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] Reproduced (for example, a stress loop running the three classes in parallel with extra measurements), or the
      hypothesis falsified and another cause found
- [x] The listeners record thread-safely, and/or the three classes share an xUnit collection; each test asserts only
      measurements it caused (tag or entity name), so a sibling's measurements cannot satisfy it either
- [x] The suite passes a stress run that failed before

## Out of scope

- Instrumentation behaviour itself: production code is not implicated by anything measured.

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-657` — leave empty until then._

## Resolution (2026-10-17)

**Hypothesis measured and confirmed** (rule 54: no longer only recorded).

- **The failure itself.** The run's artifact holds attempt 1's trx, with the failure message. The failing listener's
  `_recordedCounters` held **six** `birko.store.operation.count` entries and no `operation.errors`. The test causes
  one count and one error, so five of those counts were siblings' measurements arriving in this listener. The test's
  own error entry was lost.
- **The mechanism, reproduced.** A throwaway stress probe ran a listener shaped like the old one, `List.Add` in the
  callback, while 8 threads × 5000 `StoreInstrumentation.Execute` calls each recorded an error. Over 5 rounds it kept
  38307-38712 of 40 000 error entries, losing 3-4%, **with no exception**. Concurrent `List.Add`s overwrite each other.
- **Ruled out.** The instruments are `static readonly` fields, so no lazily created instrument races with a listener.

**Fix (test-only; production code is not implicated):**
- The six classes that publish to or listen on the meter share `[Collection(StoreMeterCollection.Name)]`, so no
  sibling measures during a test.
- The three listeners record into `ConcurrentQueue`.
- `StoreInstrumentationTests` uses a unique entity per instance and records only measurements carrying that entity
  tag, so another test's measurements cannot satisfy its assertions either.
- New `Every_measurement_is_recorded_while_other_threads_publish`: 8 threads × 2000 errors, every one counted.
  **Red 3/3 with the plain `List`**, green with the queue.

**Stress:** the full suite passed 40/40. 40 runs also passed before the fix (the flake was rare), so the deterministic
test above is the evidence, not the loop.

Documented in `tests/Birko.Telemetry.Tests/CLAUDE.md`. Suite 62/62. The suite is the only importer touched: test files
only.
