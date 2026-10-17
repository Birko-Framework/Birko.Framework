---
id: TASK-658
parent: STORY-053
feature: FEATURE-014
status: done
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

- [x] Measured live (RavenDB container): what a failed `SaveChangesAsync` leaves on the server, and what a retry does
- [x] `CommitAsync` releases the session on failure and the first `RollbackAsync` after it is a no-op, as on the other
      backends. Or, if the measurement shows a retry is safe and useful, the divergence is kept and stated in the spec
      as a decision.
- [x] A regression test fails on the current code and passes with the fix
- [x] The RavenDB requirement in `unit-of-work-and-transactions.md` is updated to match

## Out of scope

- The other backends (TASK-325, TASK-578)

## Human test plan

N/A — a library contract, asserted by tests.

## Resolution (2026-10-17)

**Measured live first** (RavenDB container, a probe and then `Task658RavenUnitOfWorkLiveTests`):

| Failure | On the server | The unit of work after it (HEAD) | A retry (HEAD) |
|---|---|---|---|
| Conflict: another cluster-wide session wrote `rows/1` first | nothing applied (`rows/1` kept the other session's value) | active | `ClusterTransactionConcurrencyException` again, forever |
| `SaveChangesAsync` with a cancelled token | nothing applied | active | **succeeds and writes the changes late** |

A retry cannot apply twice: the cluster-wide compare-exchange guards would refuse it. But it was useless after a
conflict, and after a cancellation a silent late write. That justifies no divergence, so **aligned**, not kept:
- `CommitAsync` takes the session, saves, and disposes it in a `finally`.
- A failure sets `_endedByFailedCommit`. The first `RollbackAsync` after it is a no-op, a second throws
  `NoActiveTransactionException`, and so does a retried `CommitAsync`.
- The driver's exception propagates unchanged.

To try again a caller begins anew and re-applies its changes, which a concurrency conflict needs anyway.

Red 2/2 on HEAD (`IsActive` stayed true on both). The RavenDB suite passes 116/116.

**Docs:** the spec's "A failed SaveChanges leaves the unit of work active" is inverted to "… ends the unit of work"
(rule 56), and the requirement text is updated. Noted in `Birko.Data.RavenDB/CLAUDE.md`.

Regression: all 7 importers of `Birko.Data.RavenDB.projitems` pass live, 191 tests.
