---
id: TASK-697
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P3
assignee: ai
created: 2026-10-20
depends-on: [TASK-694]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A joined localized write boundary marks the caller's unit rollback-only on a refusal a decorator above it retries

## Context

Found by [[TASK-694]]'s close review (2026-10-20), not measured. A slug or default decorator can sit above a localized
wrapper that has a `SqlLocalizedWriteBoundary`, inside a caller's `SqlUnitOfWork`:

1. The entity write is refused by the slug's unique key.
2. `SqlLocalizedWriteBoundary.RunJoined` (Birko.Data.Localization.SQL/SqlLocalizedWriteBoundary.cs ~197-212) calls
   `MarkRollbackOnly()` and rethrows.
3. The decorator above catches the refusal and retries successfully.
4. The caller's `CommitAsync` then fails, because the unit is rollback-only, although every write succeeded.

This already happened on SQL Server, which retries in place. TASK-694 makes PostgreSQL retry in place too, so it now
reaches PostgreSQL.

## Acceptance criteria

- [ ] Measure it live (PostgreSQL and SQL Server): slug decorator → localized wrapper with a boundary → SQL store,
      inside a caller's unit, with a racing writer. Record whether the commit fails (rule 54).
- [ ] If it does, decide how the boundary tells "a refusal someone above will retry" from "a write that failed". For
      example, a refusal that ran inside a retryable write's savepoint does not mark the unit. Then fix it, with the live
      test inverted.

## Out of scope

- The upsert's own retry (TASK-694).

## Human test plan

N/A — asserted by the live suites.

## Implementation plan

_Populated by `/tasks plan TASK-697` — leave empty until then._
