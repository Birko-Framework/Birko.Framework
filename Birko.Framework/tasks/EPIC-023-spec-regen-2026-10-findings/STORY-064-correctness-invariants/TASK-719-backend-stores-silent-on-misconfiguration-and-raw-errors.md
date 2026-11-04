---
id: TASK-719
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
findings: [SH-T696-14]
pr: null
github-issue: null
jira-key: null
---

# Backend stores: silent no-ops when unconfigured, raw driver errors, delegate before Guid

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. Unconfigured MongoDB, RavenDB, Cosmos DB and InfluxDB stores do nothing silently and their update-miss does not throw; MongoDB counts an unacknowledged write as a hit. Cosmos delete of a missing item throws a raw 404 `CosmosException`. Cosmos and RavenDB `SaveAsync` run the store delegate before `EnsureGuid`; a native upsert collision surfaces as the raw driver exception, not `StoreUniqueViolationException`. Cosmos batch bulk update skips Guid-less items silently.

Evidence: AsyncMongoDBStore.cs:226,251; AsyncCosmosDBStore.cs:333,436-438,560; AsyncRavenDBStore.cs (SaveAsync).

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

New cross-provider contract `tests/Shared/StoreContract/WriteEdgeContract.cs` (linked into the 12 provider suites; live
on PostgreSQL, SQL Server, TimescaleDB, MongoDB, RavenDB, Elasticsearch and the Cosmos DB emulator; MySQL skipped),
`tests/Birko.Data.MongoDB.Tests/Stores/Task719SaveCollisionLiveTests.cs` and
`tests/Birko.Data.CosmosDB.Tests/Task719BatchUpdateTests.cs`.

| Claim | Result |
|---|---|
| Unconfigured MongoDB, RavenDB, Cosmos DB and InfluxDB stores do nothing silently, and their update-miss does not throw | **true, and wider: moved to [[TASK-812]].** The SQL stores answer the same way with no connector (`if (Connector == null) return Guid.Empty;`); Elasticsearch, JSON and XML refuse. One base-level refusal is its own change |
| MongoDB counts an unacknowledged write as a hit | **true, recorded, not changed:** an unacknowledged concern returns no count, so a miss cannot be told; refusing would make it unusable for updates (SH-M306 chose this) |
| Cosmos DB delete of a missing item throws a raw 404 | **reproduced**, single and bulk, sync and async. Found: **Elasticsearch** threw too (`ElasticSearch delete failed`) |
| Cosmos DB and RavenDB `SaveAsync` run the delegate before `EnsureGuid` | **reproduced:** the delegate saw a null Guid (async; the sync stores use the base routing) |
| A native upsert collision surfaces as the raw driver exception | **reproduced on MongoDB:** `MongoWriteException` from `SaveAsync` while `CreateAsync` answered `StoreUniqueViolationException`. Cosmos DB translated the same way, not run (needs a unique-key policy) |
| Cosmos DB batch bulk update skips Guid-less items | **reproduced.** Found: **Elasticsearch** and the **async JSON and XML** bulk updates skipped them too |

## Implementation plan

Cosmos DB deletes through `DeleteIfStoredAsync` (an item 404 is a no-op), assigns the Guid before the save delegate and
translates an upsert collision; its batch bulk update reports a Guid-less item. RavenDB's `SaveAsync` assigns the Guid
first. MongoDB's upsert translates a collision. Elasticsearch treats a `not_found` delete as a no-op and reports a
Guid-less bulk update item. The async JSON and XML bulk updates report a Guid-less item as the sync ones do.

## Built (2026-11-04)

10 contract cases (5 sync, 5 async) on 12 providers, the MongoDB collision and the Cosmos DB batch: 14 runs failed on
the old code (Cosmos DB 5 and the batch, Elasticsearch 4, JSON 1, XML 1, RavenDB 1, MongoDB 1). All 12 provider suites
green live. Spec `backend-store-implementations.md` updated (scoped; `generated-at` left).
