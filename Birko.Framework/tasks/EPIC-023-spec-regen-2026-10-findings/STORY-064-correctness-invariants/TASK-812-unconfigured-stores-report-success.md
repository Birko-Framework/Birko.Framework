---
id: TASK-812
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-11-04
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-14]
pr: null
github-issue: null
jira-key: null
---

# Stores that were never configured answer writes as if they succeeded

## Context

Split from TASK-719 (2026-11-04), whose harvest finding named MongoDB, RavenDB, Cosmos DB and InfluxDB. Reading the
code shows the pattern is framework-wide, not four providers: every `*Core` write opens with a guard such as
`if (Collection == null || data == null) return Guid.Empty;` (MongoDB), `if (_container == null …) return;` (Cosmos DB)
and `if (Connector == null) return Guid.Empty;` (`AsyncDataBaseStore.CreateCoreAsync`, every SQL provider). So a store
whose `SetSettings` was never called creates nothing and returns `Guid.Empty`, updates nothing without the
`EntityNotFoundException` an update-miss raises (TASK-555), and reads return empty — the shape rule 22 forbids ("a write
that cannot be applied must never report success").

A fix belongs at the base (`AbstractStore` / `AbstractAsyncStore` and the bulk tiers, plus the SQL hierarchy, which is
separate — § Architecture): one "is this store configured" question asked by the public wrappers, each store answering
it, and an `InvalidOperationException` ("call SetSettings first") when not. Elasticsearch already refuses this way
(`ElasticSearchStoreHelper.ResolveIndexName`).

## Acceptance criteria

- [x] Measure the current answer of each provider's unconfigured store for create, update, delete, save and read
      (a contract test over every provider, like `tests/Shared/StoreContract`).
- [x] Decide reads as well as writes (an unconfigured read answering empty is the same false answer) and record it.
- [x] Every provider refuses the same way, with the contract test failing on the old code.
- [x] Consumers checked (Symbio, FisData, WorkoutTracker): nothing relies on an unconfigured store being a no-op.

## Out of scope

- The other TASK-719 findings (fixed there).

## Human test plan

N/A — covered by automated tests.

## Decision (2026-11-04, the user)

Reads refuse as writes do. Recorded because it reverses TASK-464, which kept JSON and XML reads answering empty so that
something probing a store before configuring it would not fail at start-up; that answer was false all the same.

## Measured (2026-11-04, before the fix)

New cross-provider contract `tests/Shared/StoreContract/UnconfiguredContract.cs` (sync and async, on the 12 provider
suites, live), plus `tests/Birko.Data.InfluxDB.Tests/Task812UnconfiguredTests.cs` (InfluxDB has no contract suite).

| Provider | Before |
|---|---|
| MongoDB, RavenDB, Cosmos DB, InfluxDB | **answered:** `Create` → `Guid.Empty`, `Update` / `Delete` silent, reads `null` / empty / `0` |
| JSON, XML | writes refused (TASK-464); **reads answered empty**, and the sync `Read(Guid)` skipped the gate |
| SQL (every provider), Elasticsearch | **already refused** — the context's claim about SQL (`if (Connector == null) return Guid.Empty;` in the `*Core`) did not hold: `InitCore` refuses first (TASK-170), so the `*Core` guard is never reached |
| In-memory | needs no settings |

Consumers: Symbio's Cosmos and Mongo connection factories and the Sandbox's JSON and XML probes call `SetSettings` on
the line after construction; FisData and WorkoutTracker construct none of these stores.

## Implementation plan

The SQL stores' pattern: a refusal at the top of `InitCore(Async)` in MongoDB, RavenDB, Cosmos DB, InfluxDB (both
twins) and every JSON and XML store with an `InitCore` (`RequireSettings()`). The public overrides that answered without
the gate open it: sync JSON / XML `Read(Guid)`, MongoDB `SaveAsync` and `ReadAllAsync`.

## Built (2026-11-04)

2 contract cases on 12 providers and 2 InfluxDB tests: 16 runs failing on the old code. Inverted (rule 56): 22 tests
pinning an unconfigured store's empty answers (InfluxDB 2, Cosmos DB 7, JSON 3 + 3, XML 3 + 4) and MongoDB's 4 null-filter
opt-out tests, which proved the guard let the opt-out through by its silence and now prove it by reaching the refusal.
Spec `backend-store-implementations.md` updated (scoped; `generated-at` left).
