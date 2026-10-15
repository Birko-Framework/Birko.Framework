---
id: TASK-635
parent: EPIC-014
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-15
depends-on: [TASK-628]
blocks: []
related: [TASK-622, TASK-634]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Index drift is detected for SQL only: MongoDB, RavenDB and CosmosDB have none

## Context

Filed 2026-10-15 with the user. [[TASK-622]] made SQL schema drift report a declared index the table lacks (`Missing`,
`ColumnsDiffer`, `UniquenessDiffers`), measured on all four providers. The document stores have no equivalent: an index a
model declares can be absent, or differ, on MongoDB, RavenDB and CosmosDB, and nothing says so. On a document store a
missing index is mostly a performance failure, but a missing **unique** index is a correctness one: duplicates are
accepted silently (the storage-level enforcement [[TASK-632]] may rely on).

CosmosDB cannot answer "does index X exist" by name at all until [[TASK-628]] gives its index manager a name registry,
hence the dependency.

## Acceptance criteria

- [x] Each provider reports declared-vs-stored index drift with the same kinds as TASK-622, read through its index
      manager, measured live (MongoDB, RavenDB; CosmosDB offline or on an emulator if one becomes available).
- [x] The ElasticSearch / SQL health checks' pattern is followed, so a host can surface it at startup.
- [x] Detection only: creating or changing an index stays the operator's (or a migration's).

## Out of scope

- Repair statements (a later task, if detection shows the need).

## Resolution (2026-10-15)

**The declared side had to be decided first.** SQL derives declared indexes from the model (`[IndexedField]`, fluent
keys). The document stores have no model-level index declaration at all: the only portable one, the fluent mapping, is
`Birko.Models.SQL`. An index on MongoDB, RavenDB or Cosmos DB exists because a host or a migration created it from an
`IndexDefinition`, so that list is the declared side, and the stored side is the provider's own `IIndexManager`.

- **`IndexDeclarationDriftDetector.DetectAsync(manager, declared, scope)`** (Birko.Data.Patterns.IndexManagement):
  `Missing`, `ColumnsDiffer` (fields, order, direction, field type) and `UniquenessDiffers`, the TASK-622 kinds, plus
  `Unverified` for an index the provider reports no fields for, or whose lookup threw. "Could not compare" is never
  clean. One detector for all providers, rather than one per provider.
- **`IndexDeclarationDriftHealthCheck`**, new project **Birko.Health.Data.IndexManagement** (Birko.Health.Data stays
  dependency-free): Degraded on drift or on anything not compared, Unhealthy only if a probe throws. The pattern of the
  SQL and ElasticSearch drift checks.

**Two index-manager defects, found by the live measurement and fixed (both red before the fix):**
- **MongoDB:** a text index's key is `{_fts: "text", _ftsx: 1}`, so `GetInfoAsync` / `ListAsync` described it as fields
  `_fts` and `_ftsx`; every text index read as `ColumnsDiffer`. The fields are now read from the index's `weights`.
- **RavenDB:** `GetInfoAsync` of an index that does not exist threw `InvalidOperationException` ("Response is invalid.")
  instead of returning null, so the detector could not tell a missing index from a failed lookup. It now looks the name
  up first. RavenDB stores a map, not a field list, so `GetInfoAsync` also reports the fields of a map in the form
  `CreateAsync` generates (one producer, `GeneratedMap`); a hand-written map has no fields and is "not compared".

**Measured live:** MongoDB (indexes created from the declarations, text included, are clean; a dropped index is
`Missing`, a text index with another field set `ColumnsDiffer`, a unique index recreated plain `UniquenessDiffers`);
RavenDB (clean; `Missing`, `ColumnsDiffer`; a declared unique index is `Missing`, as RavenDB refuses to create one; a
custom map is not compared); Cosmos DB on the emulator (clean; `Missing`, `ColumnsDiffer`; a policy path no name was
registered for is not compared). Offline: each kind, the uncompared cases, and the health check's four outcomes.

**Regression:** the 16 suites importing the MongoDB, RavenDB or CosmosDB projects plus the new one (479 tests), live,
green. The whole solution builds.

## Human test plan

N/A: asserted against live servers.
