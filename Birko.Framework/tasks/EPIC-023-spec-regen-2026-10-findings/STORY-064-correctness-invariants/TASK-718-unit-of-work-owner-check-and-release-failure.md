---
id: TASK-718
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
findings: [SH-T696-13]
pr: null
github-issue: null
jira-key: null
---

# SqlUnitOfWork reads rollback-only through the caller's flow; a failed savepoint release reports an applied write as failed

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `SqlUnitOfWork.CommitAsync` checks rollback-only through `AmbientSqlTransaction.Find` in the calling flow, not its own entry, so a commit from a flow whose cell no longer resolves the entry misses a participant's mark (Mongo and Raven check their own scope). `SqlSavepoint` reports failure if `Release` throws after the write applied. A Mongo unit of work built from a bare `IMongoClient` publishes nothing.

Evidence: Birko.Data.SQL/UnitOfWork/SqlUnitOfWork.cs:150; Birko.Data.SQL/Stores/SqlSavepoint.cs; MongoDbUnitOfWork.cs:47-50.

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

`tests/Birko.Data.SQL.SqLite.Tests/Task718UnitOfWorkRollbackOnlyTests.cs` (SQLite) and
`tests/Birko.Data.SQL.Tests/UnitOfWork/Task718SavepointReleaseTests.cs` (a transaction whose `Release` throws).

| Claim | Result |
|---|---|
| `SqlUnitOfWork.CommitAsync` reads rollback-only through `Find` in the caller's flow | **reproduced:** an owner committed from a flow queued with the execution context suppressed committed over a participant's rollback |
| `SqlSavepoint` reports failure when `Release` throws after the write applied | **reproduced**, sync and async: the release's exception reached the caller of an applied write |
| A Mongo unit of work from a bare `IMongoClient` publishes nothing | **true, recorded, not changed:** a bare driver client names no Birko database, and a key derived from its settings would not match a store's settings id. The constructor now says so; its `Context` is handed to stores |

## Implementation plan

`AmbientSqlTransaction.Enter` gains an overload handing back the entry it published; the owner keeps it and reads its
mark. `SqlSavepoint` releases through `TryRelease`, which does not fail an applied write (nor replace an
`EntityNotFoundException`).

## Built (2026-11-04)

4 tests: the 3 measurements (failing on the old code) and a guard that a failing write still rolls back and throws.
SQL 726, SqLite 597 green, and the 33 other suites importing Birko.Data.SQL plus MongoDB with PostgreSQL and SQL Server
live. Spec `unit-of-work-and-transactions.md` updated (scoped; `generated-at` left).
