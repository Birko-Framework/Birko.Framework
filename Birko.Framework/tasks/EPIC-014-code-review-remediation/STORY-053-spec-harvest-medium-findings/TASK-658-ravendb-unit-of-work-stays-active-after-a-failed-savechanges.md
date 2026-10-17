---
id: TASK-658
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-17
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The RavenDB unit of work stays active after a failed `SaveChanges`, unlike every other backend

## Context

Found while fixing [[TASK-578]]. The spec `unit-of-work-and-transactions.md` records, in the RavenDB requirement, the
scenario "A failed SaveChanges leaves the unit of work active": `RavenDbUnitOfWork.CommitAsync`
(`Birko.Data.RavenDB/UnitOfWork/RavenDbUnitOfWork.cs:89-91`) awaits `SaveChangesAsync(ct)` and only then disposes and
drops the session. So a throw (a concurrency conflict, a cancellation) leaves the session held and `IsActive` true.

SQL and Elasticsearch (TASK-325), and since TASK-578 MongoDB, Cosmos DB and InfluxDB, all end the boundary on a failed
commit, release what they hold and make the `RollbackAsync` that follows a no-op. RavenDB is the last exception. Here a
retried `CommitAsync` re-sends the same tracked changes. Whether that is a defect depends on cluster-wide transactions:
a failed cluster-wide `SaveChanges` applied nothing. **Measure first** whether a retry after a failure can apply twice,
or whether only the lifecycle is inconsistent (rule 54).

## Acceptance criteria

- [ ] Measured live (RavenDB container): what a failed `SaveChangesAsync` leaves on the server, and what a retry does
- [ ] `CommitAsync` releases the session on failure and the first `RollbackAsync` after it is a no-op, as on the other
      backends. Or, if the measurement shows a retry is safe and useful, the divergence is kept and stated in the spec
      as a decision.
- [ ] A regression test fails on the current code and passes with the fix
- [ ] The RavenDB requirement in `unit-of-work-and-transactions.md` is updated to match

## Out of scope

- The other backends (TASK-325, TASK-578)

## Human test plan

N/A — a library contract, asserted by tests.
