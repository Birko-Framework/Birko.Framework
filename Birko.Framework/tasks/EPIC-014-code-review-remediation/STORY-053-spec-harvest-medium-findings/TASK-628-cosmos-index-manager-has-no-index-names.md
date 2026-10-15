---
id: TASK-628
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
findings: [SH-M433]
pr: null
github-issue: null
jira-key: null
---

# The Cosmos DB index manager cannot find, by name, an index it created

## Context

Spawned by [[TASK-154]] (2026-10-15), which confirmed SH-M433 by reading `CosmosDBIndexManager` and left it unfixed
because the fix is a design decision, not a correction.

A Cosmos DB indexing policy has **no index names**: an index is an included path or a composite path list.
`CreateAsync(definition)` writes the paths and discards `definition.Name`, so:

- `ExistsAsync("IX_Code", container)` is false for the index `CreateAsync` just made from `IX_Code`, because only a
  path (`/code/?`) can match. A migration guarding a create with `ExistsAsync` recreates on every run.
- `DropAsync` removes only an included path; a composite index can never be dropped, and the listing names composites
  `composite_<i>`, which shifts when one is removed.
- `Unique`, `Sparse` and `ExpireAfter` are dropped silently (rule 55). Unique keys exist on Cosmos only at container
  creation, TTL is a container property, and there is no sparse index.

## What to decide

1. **What an index name means on Cosmos.** Either the name IS the path set (a convention `CreateAsync` enforces, so a
   free-form name is refused), or the framework keeps a name → paths registry somewhere durable (the container cannot
   carry custom metadata, so a document). The first is honest and small; the second keeps portable migration code
   working unchanged.
2. Whether `Unique` maps to a container unique key when the container is created by this manager, or is always refused.

## Decisions (2026-10-15, the user)

- **Unique:** honoured as a container unique key when this manager creates the container, refused
  (`NotSupportedException`) on an existing container. **Sparse** and **ExpireAfter** are always refused: Cosmos has no
  sparse index, and TTL is a container property, not an index's.
- **Names: B, a name registry in a separate metadata container.** One document per indexed container, in a dedicated
  container of the same database (`__birko_indexes`, TASK-272's `__` prefix for bookkeeping on schema-less providers),
  mapping each index name to its included paths or composite path list. The indexing policy stays the truth: `ExistsAsync`
  answers true only when the registered paths are in the policy, `DropAsync` removes exactly those paths (composites
  included) and the entry, and a listing reports registered names plus unnamed policy entries by path. Entity reads never
  see the registry, so no store changes. Cost: one extra container per database.

## Acceptance criteria

- [x] `ExistsAsync` / `DropAsync` / `GetInfoAsync` by the name `CreateAsync` was given answer for that index, composites included
- [x] `Unique`, `Sparse` and `ExpireAfter` are honoured or refused, never dropped
- [x] Measured against the Cosmos emulator (gateway mode, as the live suites run)

## Resolution (2026-10-15)

**Names: B, as decided.** `CosmosDBIndexManager` keeps one document per indexed container in `__birko_indexes`
(`RegistryContainerName`, same database, partitioned on `/id`), mapping each name to its included path or composite
path list and orders. It is read and written as raw JSON through the stream API, so it does not depend on the
serializer a consumer set on the client, and with an ETag check retried on a concurrent write. The policy stays the
truth:
- `ExistsAsync` is true for a registered name only while its paths are in the policy.
- `DropAsync` removes exactly its paths (a composite included), unless another registered name stands for the same
  paths, and then the entry.
- `ListAsync` reports registered names, then unregistered policy entries by path; a composite is named by its path list
  (`(/C ASC, /D DESC)`), which does not shift.
- A name nothing registered is still answered by path, as before.
- Re-creating the same definition is idempotent; the same name with other fields throws `IndexManagementException`.
  Creating an index a drop had excluded lifts the exclusion.

**Unique: decision changed on measurement, with the user (2026-10-15).** The decision was to honour `Unique` as a
container unique key when the manager creates the container. Measured on the emulator before shipping it: a container
created with the unique key `/Code` accepted a second document with the same `Code` (201). A Cosmos unique key holds
within a logical partition, and the stores partition only on `/id` (TASK-614), so every document is its own partition
and the key rejects nothing. Honouring it would report `Unique` while enforcing nothing, so the user asked for the best
option and it is **refused** (`NotSupportedException`, the message says why). The measurement is kept as a test
(`A_unique_key_on_an_id_partitioned_container_rejects_nothing`), so a change in that behaviour is seen. With no unique
key to declare, the manager does not create containers: a missing container is an error, as before.

**Also refused, as decided or by the same rule:** `Sparse`, `ExpireAfter`, a non-standard field type (spatial is
`AddSpatialIndexAsync`), a single descending field (a range index has no direction; it was silently made ascending),
and any `Properties`. Each is refused before anything is written.

**Measured live, Cosmos emulator (vnext-preview, gateway, HTTP), 14 tests:** single-field and composite indexes are
found, described and dropped by name; an unregistered composite keeps its name when another is dropped; a shared path
survives dropping one of its two names; re-create, conflicting re-create and re-create after drop; the registry is its
own container and a query on the indexed container returns no registry document; each refusal leaves policy and
registry untouched. **Red:** against the old manager, 12 of 14 fail; the 2 that pass either way are the unregistered
path pin and the unique-key measurement.

**Regression:** the 7 suites importing `Birko.Data.CosmosDB` (205 tests), live, green.

**Not in scope, left as found:** the migration builder `CosmosIndexBuilder.Build()` still refuses (TASK-274), so a
Cosmos migration cannot create an index through `context.Schema`; wiring it to this manager belongs to [[TASK-152]]
(SH-M174/M184-186). Index drift detection for Cosmos is [[TASK-635]], now unblocked.

## Human test plan

N/A: asserted against the emulator.
