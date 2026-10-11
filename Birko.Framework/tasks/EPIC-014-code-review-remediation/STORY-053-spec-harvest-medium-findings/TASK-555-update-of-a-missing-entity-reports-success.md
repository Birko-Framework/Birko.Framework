---
id: TASK-555
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [SH-M306, SH-M309]
pr: null
github-issue: null
jira-key: null
---

# `Update` of an entity that is not stored reports success on every backend, and `Save` hands back its Guid

## Context

Spawned from [[TASK-163]] (2026-10-11) while verifying `SH-M306` / `SH-M309`. Both were filed against the in-memory
store; **both are confirmed, and broader than filed**: nothing in the framework can report "no row matched".

- `IUpdateStore<T>.Update` returns `void`. Every backend no-ops on a missing key:
  `AbstractInMemoryStore.UpdateCore` / `AbstractJsonStore.cs:87` / XML (`ContainsKey` guard, nothing written),
  `DataBaseStore.cs:209` (an `UPDATE … WHERE pk = …` that matches 0 rows), `MongoDBStore.cs:163`
  (`ReplaceOne(… IsUpsert = false)`, `MatchedCount` never read).
- `AbstractStore.Save` (`AbstractStore.cs:233`) routes a non-empty Guid to `Update` and returns `data.Guid!.Value`
  (async twin `AbstractAsyncStore.SaveAsync`). So `Save(entityWithClientAssignedGuid)` — a natural way to insert
  an entity whose id the caller chose — writes nothing and returns a plausible identifier.
- The bulk `Update(IEnumerable)` applies partially with no report of which items were skipped.

Not fixed in TASK-163 because a fix is a **contract decision across ~20 providers**, not an in-memory edit: making
the in-memory double strict alone would make it diverge from every real backend it stands in for.

## The decision to take first

1. **`Update` of a missing entity** — throw (`StoreException`, or a typed `EntityNotFoundException`), or return a
   count/bool (a breaking signature change across `IUpdateStore` / `IAsyncUpdateStore` and every wrapper).
2. **`Save` with a Guid that is not stored** — upsert (insert it under the caller's Guid), or throw. Upsert matches
   what `Save` is used for; it needs a per-provider native upsert or a read-then-write inside the store's boundary.

## Decision (2026-10-11, by the user)

1. **`Update` of a missing entity throws** a typed `EntityNotFoundException` (a `StoreException`); `Update` stays
   `void`. Each provider reads its native matched/affected count — no pre-read. The bulk `Update(IEnumerable)` throws
   too, naming the missing ids.
2. **`Save` with a Guid that is not stored upserts** — inserts the entity under the caller's Guid (native upsert where
   the provider has one, otherwise update-then-create-on-miss inside the store's boundary).

The task is no longer a decision task: it is runnable by [[fix-next]].

## Acceptance criteria

- [ ] The two decisions above recorded (here, and in `docs/specs/store-crud-contract.md` after regen)
- [ ] Every provider implements them — in-memory, JSON, XML, SQL (all four + SQLite), MongoDB, RavenDB, CosmosDB,
      ElasticSearch, InfluxDB — each reading its native matched/affected count rather than a pre-read
- [ ] A shared contract test (one fixture, run per provider) proves: `Update(missing)` behaves as decided;
      `Save(new entity with a client Guid)` behaves as decided; bulk `Update` with one missing item does not report
      success silently. Red-verified per provider
- [ ] CHANGELOG migration entry (this is observable to every consumer that relied on the silent no-op)
- [ ] `/specs regen store-crud-contract`; the "Save returns the Guid of an entity it never stored" scenario changes

## Out of scope

- Identity assignment on create (null / `Guid.Empty` / overwrite) — [[TASK-556]].
- `StoreDataDelegate<T>`'s ignored return value — [[TASK-557]].

## Human test plan

N/A — a library contract with no UI surface; fully covered by the per-provider contract tests.

## Implementation plan

_Populated by `/tasks plan TASK-555` — leave empty until then._
