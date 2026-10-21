---
id: TASK-718
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [ ] Each reproduced defect fixed, with a regression test that fails without the fix.
- [ ] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Implementation plan

_Populated by `/tasks plan TASK-718` — leave empty until then._
