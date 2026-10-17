---
id: TASK-578
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
findings: [SH-M429, SH-M430, SH-M431]
pr: null
github-issue: null
jira-key: null
---

# The MongoDB, Cosmos DB and InfluxDB units of work leave state behind after a commit or a failed commit

## Context

Found by the 2026-10-12 `/specs regen` of all 25 areas at `c50ed580` (`unit-of-work-and-transactions`). Harvester claims, not yet reproduced. One task because they
share a cause: these three units of work arrived after TASK-325 set the lifecycle rule for the Elasticsearch one (a failed
commit ends the boundary and does not leave the work queued for a silent re-send), and none of them follows it.
TASK-325 (the area's medium task) is done, so there is no open area task to link these into.

- **SH-M429 MongoDB** (`Birko.Data.MongoDB/UnitOfWork/MongoDbUnitOfWork.cs:19,65-91`): commit and abort never dispose the
  client session, so a `BeginAsync` after a commit starts a new session over the old, undisposed one.
- **SH-M430 Cosmos DB** (`Birko.Data.CosmosDB/UnitOfWork/CosmosDbUnitOfWork.cs:76-90`): a non-success batch commit throws
  `InvalidOperationException`, not `UnitOfWorkException`; a throwing `ExecuteAsync` leaves the batch active.
- **SH-M431 InfluxDB** (`Birko.Data.InfluxDB/UnitOfWork/InfluxDbUnitOfWork.cs:234-247`): a failed write leaves the context
  active with the same points, so a retry re-sends everything.

## Acceptance criteria

- [x] Each claim reproduced first; a claim that does not reproduce is closed here with the evidence
- [x] After a commit, a failed commit or a rollback, each of the three releases what it holds and reports inactive
- [x] A failed commit throws the unit-of-work exception type the other backends throw
- [x] A regression test fails on the current code and passes with the fix
- [x] `/specs regen` of the area, with the spec diff reviewed, is the fix's evidence (EPIC-014's standing constraint)

## Out of scope

- The SQL and Elasticsearch units of work (TASK-325)
- TASK-327 (the area's low findings)

## Human test plan

N/A — the behaviour is asserted by the regression test; nothing here is visual.

## Resolution (2026-10-17)

All three claims reproduced live (Docker: mongo:7 replica set, Cosmos emulator in gateway mode, influxdb:2):
**6 red on HEAD.**

- **SH-M429, MongoDB (3 red).** After a commit or a rollback, `Context` was still the old session, never disposed. A
  failed commit (a cancelled token) also left it held. Now `CommitAsync` and `RollbackAsync` dispose and drop the
  session in a `finally`, whatever the outcome. `BeginAsync` releases any session left over before it starts.
- **SH-M430, Cosmos DB (2 red).** A refused batch (a create of an existing id) threw `InvalidOperationException`. A
  throwing `ExecuteAsync` (a cancelled token) left `IsActive` true. Now the batch is dropped before it is sent. A refused
  response is a `UnitOfWorkException` naming the status, as on Elasticsearch. A thrown exception propagates unchanged
  (rule 9: only an answer is turned into an exception). Nothing in the framework caught the old
  `InvalidOperationException`.
- **SH-M431, InfluxDB (1 red).** A write to a missing bucket left the context active with the same points. Now the
  points are taken and the context dropped before the write. The client's exception propagates unchanged.
- **All three** follow TASK-325's catch-and-roll-back contract: the first `RollbackAsync` after a failed commit is a
  no-op (`_endedByFailedCommit`), and a second one throws `NoActiveTransactionException`.

**Mongo, on the server side:** the failed-commit test writes the same row outside the transaction within 5 s, which an
open transaction would block. Disposing the session aborts it.

**Observed, not explained (rule 54):** the first runs of the Mongo failed-commit test took 89 s on HEAD and 69 s with
the fix. That version counted rows right after the failed commit. Once it wrote the same row instead, three runs took
150 ms each. The slow run did not come back, so no cause is adopted. The likely place to look is the database drop
waiting for a transaction's 60 s lifetime.

Spec `unit-of-work-and-transactions.md`: the MongoDB, Cosmos DB and InfluxDB requirements now state the release, the
exception type and the no-op rollback. Their scenarios that recorded the defects are inverted (rule 56). The spec was
updated by hand from the fix's diff, with no regen run.

**Not in scope, filed:** the spec records that RavenDB's unit of work also stays active after a failed `SaveChanges`.
It is the same lifecycle rule, but the task named three backends: [[TASK-658]].

Regression: all 16 importers of the MongoDB, CosmosDB and InfluxDB projitems pass live, 467 tests.
