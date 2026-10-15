---
id: TASK-628
parent: STORY-053
feature: FEATURE-014
status: todo
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

## Acceptance criteria

- [ ] `ExistsAsync` / `DropAsync` / `GetInfoAsync` by the name `CreateAsync` was given answer for that index, composites included
- [ ] `Unique`, `Sparse` and `ExpireAfter` are honoured or refused, never dropped
- [ ] Measured against the Cosmos emulator (gateway mode, as the live suites run)

## Human test plan

N/A: asserted against the emulator.
