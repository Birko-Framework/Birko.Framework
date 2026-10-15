---
id: TASK-627
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
findings: [SH-M245]
pr: null
github-issue: null
jira-key: null
---

# A migration that forgets `Build()` performs no DDL and is recorded as applied

## Context

Spawned by [[TASK-154]] (2026-10-15), which confirmed SH-M245 and found its proposed remedy wrong.

`IIndexBuilder.Build()` and `ICollectionBuilder.Build()` are default interface methods with empty bodies. The SQL,
MongoDB, RavenDB, CosmosDB, ElasticSearch and InfluxDB builders defer their work to `Build()`, so
`CreateIndex("Invoice", "IX_Number").WithField("Number")` without the terminal call emits nothing, returns normally,
and the runner records the migration as applied: a schema change permanently missed, with no error.

**The finding's fix does not fix it.** "A non-defaulted Build() would have made the omission a compile error" is
false: making `Build()` abstract forces *implementers* to declare it, but a *caller* that never calls it compiles
either way. The omission is only detectable at run time, when the migration ends.

## What to do

Every schema builder tracks the builders it handed out; when a migration's `Up` / `Down` returns, the runner asks
the schema builder, and any builder that was never built fails the migration **before** it is recorded, naming the
collection and index. There is no completion hook today (`IMigrationContext` exposes only `Schema`), so this adds
one, across the six providers.

## Acceptance criteria

- [x] A migration with an unterminated `CreateIndex` / `CreateCollection` chain fails, naming it, and is not recorded
- [x] A terminated chain behaves exactly as today, on every provider
- [x] Red on the current code, measured on SQLite and at least one document store

## Resolution (2026-10-15)

**One check, in one place.** `SchemaBuildLedger` (Birko.Data.Patterns) wraps the builder that `CreateIndex` /
`CreateCollection` returns: the wrapper forwards every call to the provider's own builder and stays the chain's
receiver, and its `Build()` marks the chain finished. `ISchemaBuilder.PendingBuilds` (a default interface member,
empty for a builder that does not track) lists the unfinished ones; the six provider builders (SQL, MongoDB, RavenDB,
CosmosDB, ElasticSearch, InfluxDB) each keep a ledger, a two-line change. `AbstractMigrationRunner.ApplyMigration`
runs `Up` / `Down` and throws `MigrationException` naming each pending chain; the seven runners (TimescaleDB's
override included) call it instead of `Up` / `Down`, so the throw lands before the record, inside each runner's
existing failure path.

**Decision: the rule is uniform.** On the five providers that create a collection eagerly, an unbuilt
`CreateCollection` did create it, but the same migration creates nothing on SQL. It now fails everywhere, so a
migration that passes on one provider passes on all. No migration in the framework or its consumers relied on the
omission (Symbio's `DeclaredIndexes` always builds; its 66 migration and index tests pass).

**Measured.** SQLite (file database, through `SqlMigrationRunner`): an unbuilt index and an unbuilt table each fail the
migration, are not recorded, and leave no index or table; all unfinished chains are named and a built one is not; a
`Rollback` whose `Down` leaves a chain fails and keeps the version. Live MongoDB: the same for an index, and for a
collection on a provider that creates it eagerly. **Red:** with the check disabled, 6 of 8 runner tests fail; the 2
terminated-chain tests pass either way (contract pins). A run is one transaction on SQL, so the tests run the
preceding migration in its own run, as before.

**Regression:** the 10 suites importing a migration project (1,115 tests), live, green. Symbio builds.

## Human test plan

N/A: asserted by the migration record.
