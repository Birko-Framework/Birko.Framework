---
id: TASK-812
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Measure the current answer of each provider's unconfigured store for create, update, delete, save and read
      (a contract test over every provider, like `tests/Shared/StoreContract`).
- [ ] Decide reads as well as writes (an unconfigured read answering empty is the same false answer) and record it.
- [ ] Every provider refuses the same way, with the contract test failing on the old code.
- [ ] Consumers checked (Symbio, FisData, WorkoutTracker): nothing relies on an unconfigured store being a no-op.

## Out of scope

- The other TASK-719 findings (fixed there).

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-812` — leave empty until then._
