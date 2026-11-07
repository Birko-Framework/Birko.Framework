---
id: TASK-825
parent: EPIC-014
feature: null
status: done
priority: P1
assignee: ai
created: 2026-11-07
depends-on: []
blocks: []
related: [TASK-276, TASK-595, TASK-691, TASK-712]
findings: []
pr: null
github-issue: null
jira-key: null
---

# ModelMapRegistry.ApplyToDatabase publishes a table before its fluent indexes are on it

## Context

Reported by the Symbio session (Symbio TASK-998, 2026-11-07), reproduced there on purpose: a thread looping
`SymbioModelMappings.Apply()` and 4 threads looping `DataBase.LoadTable(typeof(EntityTranslation))` for 5 s; 3 of 3 runs,
31-33 % of ~45 M loads had no named indexes and ~350 per run threw reading `Indexes` ("Destination array is not long
enough"). Real effect: Symbio's gate created `EntityTranslations` twice (2026-10-26, 2026-11-07) with no
`UX_EntityTranslations_Key`, because xUnit runs classes calling `Apply()` in parallel. In production `Apply` runs once at
boot; the window there is a store touching a table before or while the host applies.

Mechanism (read from the code):
1. `ApplyToDatabase` → `RegisterTableNames` → `RegisterTableName` always evicts the cached table; unlike
   `RegisterMaxLength` / `RegisterPrecision` / `RegisterColumnFlags` it has no "unchanged → no-op". A mapped schema is
   registered twice per apply (name without schema, then with it), so even a no-op check per call would evict.
2. `LoadTable` rebuilds and **publishes** a bare table (the TASK-276 lock covers the build only).
3. Only then are the fluent column renames, keys, auto-increment and indexes written onto the published table, into a
   plain `Dictionary` with no lock.
Any other eviction (a later changed `RegisterMaxLength`) also rebuilds the table without the fluent indexes, for good.

## Acceptance criteria

- [x] Reproduced in a Birko test.
- [x] `RegisterTableName` with an unchanged name and schema changes nothing; `ApplyToDatabase` registers each table once.
- [x] The fluent part of a mapping is applied inside the table build, before the table is published, so no reader sees
      a table without it and nothing mutates a published table; re-applying the same mappings evicts nothing.
- [x] Regression suites of the importers of Birko.Data.SQL and Birko.Models.SQL pass.

## Human test plan

N/A — fully covered by automated tests (Symbio re-runs its TranslationTableIndexRaceTests 3x once this is on main).

## Outcome (2026-11-07)

**Reproduced** (`tests/Birko.Models.SQL.Tests/Task825ApplyPublishRaceTests.cs`, all 5 red against the original): one
thread re-applying a mapping and 4 loading the table for 1.5 s, 17.8 M of 21.2 M loads (84%) saw no fluent indexes;
re-applying the same mappings, or registering the same table name, replaced the cached table; a schema-mapped table was
replaced on every apply; and a table rebuilt after a later eviction (a changed `RegisterMaxLength`) had no fluent indexes.

**Fixed:**
- `DataBase.RegisterTableName` with an unchanged name and schema returns without evicting, like the other `Register*`.
- `ApplyToDatabase` registers each table once, with its schema (it went without the schema, then with it).
- New `DataBase.RegisterTableConfigurator(type, signature, configure)`: the build runs it before publishing the table.
  `ApplyToDatabase` registers the fluent column names, keys, auto-increment and indexes through it, from a snapshot of the
  mapping, then loads the table so a mapping that cannot be honoured is still refused at apply (the registration is
  removed again). The same signature again evicts nothing; a changed one evicts. Nothing writes to a published table.
- One configurator per type, the last wins. Before, the re-registration's eviction also dropped another registry's
  fluent part, so two registries mapping one type were not supported either.

**Tests:** 5 red → green. Regression: every test project importing Birko.Data.SQL or Birko.Models.SQL (35) passes.
