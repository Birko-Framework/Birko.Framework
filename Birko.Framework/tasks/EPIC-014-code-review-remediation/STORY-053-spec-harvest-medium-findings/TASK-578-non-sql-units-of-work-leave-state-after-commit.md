---
id: TASK-578
parent: STORY-053
feature: FEATURE-014
status: todo
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

- [ ] Each claim reproduced first; a claim that does not reproduce is closed here with the evidence
- [ ] After a commit, a failed commit or a rollback, each of the three releases what it holds and reports inactive
- [ ] A failed commit throws the unit-of-work exception type the other backends throw
- [ ] A regression test fails on the current code and passes with the fix
- [ ] `/specs regen` of the area, with the spec diff reviewed, is the fix's evidence (EPIC-014's standing constraint)

## Out of scope

- The SQL and Elasticsearch units of work (TASK-325)
- TASK-327 (the area's low findings)

## Human test plan

N/A — the behaviour is asserted by the regression test; nothing here is visual.
