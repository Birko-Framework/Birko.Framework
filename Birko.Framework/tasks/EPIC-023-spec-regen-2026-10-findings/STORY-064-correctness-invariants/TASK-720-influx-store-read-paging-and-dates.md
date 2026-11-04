---
id: TASK-720
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-15]
pr: null
github-issue: null
jira-key: null
---

# InfluxDB store: reads skip init, page before filtering, and return dates in local time

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `Read(Guid)` skips the lazy-init and cancellation gate and wraps cancellation in `InvalidOperationException`. The bulk read pages before it filters, so a filtered `limit` returns fewer rows; sorting uses only the first field's direction. `DateTime` is stored as a string and read back with `DateTime.Parse`, so a UTC value probably comes back local. A duplicate create is never refused (store contract).

Evidence: Birko.Data.InfluxDB/Stores/AsyncInfluxDBStore.cs:99,126,218-243,404-447,692,781; InfluxDBStore.cs:125.

## Acceptance criteria

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-04, before the fix)

`tests/Birko.Data.InfluxDB.Tests/Task720InfluxReadTests.cs`. No live store fixture: the store authenticates by token
only, so a local HTTP listener stands in for the query API, recording each Flux it is sent and answering a fixed
annotated-CSV table.

| Claim | Result |
|---|---|
| `Read(Guid)` skips the lazy-init gate | **reproduced**, sync and async: `InitCore` never ran |
| … and wraps a cancellation in `InvalidOperationException` | **reproduced:** `InvalidOperationException` around `TaskCanceledException`. Every other operation did the same |
| The bulk read pages before it filters | **reproduced:** a filtered `limit: 1, offset: 1` sent both to the server and filtered what came back |
| Sorting uses only the first field's direction | **reproduced:** `ByDescending(Rank).ThenBy(Name)` sent `sort(columns: ["Rank", "Name"], desc: true)` |
| A UTC `DateTime` comes back local | **reproduced:** `DateTime.Parse` of `2026-01-01T10:00:00.0000000Z` gave `Kind = Local` |
| A duplicate create is never refused | **true, recorded, not changed:** a point for a stored Guid is a newer version of it; refusing costs a query on every create. Decided 2026-11-04 by the user: accepted, as TimescaleDB's (TASK-567) — a refusal could not be atomic and would cost a read of the bucket's history per create; pinned by `InfluxDuplicateCreateTests` |

## Implementation plan

New `InfluxReadPlan` (one producer for both stores, rule 16): the bulk read's Flux, the in-memory finish (filter, then
`OrderByHelper.ApplyTo`, offset, limit) when there is a filter or a mixed-direction sort, and the `DateTime` parse
(`RoundtripKind`). `Read(Guid)` opens the gate; no catch rewraps an `OperationCanceledException`.

## Built (2026-11-04)

6 tests, all failing on the old code. InfluxDB 35, InfluxDB.ViewModel 5 green. Spec `backend-store-implementations.md`
updated (scoped; `generated-at` left).
