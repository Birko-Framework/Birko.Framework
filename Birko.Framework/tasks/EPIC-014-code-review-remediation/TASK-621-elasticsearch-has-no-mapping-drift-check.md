---
id: TASK-621
parent: EPIC-014
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
related: [TASK-613, TASK-615, TASK-269]
findings: []
pr: null
github-issue: null
jira-key: null
---

# ElasticSearch has no mapping drift check, so an index mapped differently from the model is invisible

## Context

Found 2026-10-15 reviewing schema-drift coverage. The SQL providers have `DetectDrift` and `SchemaDriftHealthCheck`;
the non-SQL stores have nothing. On ElasticSearch that has already cost a defect: TASK-613's `.keyword` bug was a stored
mapping (a `keyword` field with no `.keyword` sub-field) disagreeing with what the query side assumed, and nothing
reported it.

An index is created once (`AutoMap`) and never revisited. A model changed later, such as a property gaining
`[Keyword]`, a renamed `Name`, or a string that used to be dynamically mapped as `text` and is now meant to be exact,
keeps the old mapping. Filters and sorts are then translated for the declared mapping and run against the stored one.
Typical outcomes are silently empty results (rule 48's tell) or a sort 400.

## Acceptance criteria

- [ ] Measured first: an index created from an older model (a property mapped `text` with a `.keyword` sub-field)
      queried after the model marks it `[Keyword]` returns nothing, with no error. Record it.
- [ ] A drift report for an ElasticSearch store: declared mapping (from the same `AutoMap` the store creates the index
      with, rule 63) against `GET <index>/_mapping`. It reports missing, unexpected and differently-typed fields,
      including keyword vs text and a missing `.keyword` sub-field.
- [ ] Wired into a health check the way `SchemaDriftHealthCheck` is (Degraded, never Unhealthy), or the reason it is
      not, recorded.
- [ ] Live tests against ES 7.17.25: a clean control and each drift kind, all proven red.

## Out of scope

- MongoDB, CosmosDB and RavenDB. They are schemaless for fields; if one of them has an equivalent (indexes, CosmosDB
  partition and indexing policy), file it separately after measuring.

## Human test plan

N/A — covered by the live provider tests.

## Implementation plan

1. **Measure** on ES 7.17.25: create the index from model v1 (a dynamic `string` → `text` plus `.keyword`), then
   query with model v2 (`[Keyword]` on that property). Record the silently empty result.
2. **Declared mapping, one producer** (rule 63): build it with the same `AutoMap()` descriptor `InitAsync` uses,
   serialised through the client (`client.RequestResponseSerializer`), never a hand-written expectation.
3. **Stored mapping**: `GET <index>/_mapping` through NEST (`Indices.GetMappingAsync`).
4. **Compare** field by field on the flattened property paths: missing, unexpected, type differs (`keyword` vs
   `text`, `long` vs `keyword`, …), and a declared `.keyword` sub-field the store lacks. Put it on the stores as
   `DetectMappingDriftAsync()`, returning a report shaped like `SchemaDriftReport` (supported / exists / drifts).
   Reuse that type if it can live outside `Birko.Data.SQL`; otherwise add a small parallel one. Decide after reading
   the dependency graph (CLAUDE-projects.md), since `Birko.Data.ElasticSearch` must not reference the SQL projects.
5. **Health check**: add `ElasticSearchMappingDriftHealthCheck` beside the existing ES health check (Degraded, never
   Unhealthy, "could not determine" never Healthy), or record why not.
6. Tests: live clean control, one per drift kind (each proven red by construction of the old index), offline compare
   tests on fixture mappings.

