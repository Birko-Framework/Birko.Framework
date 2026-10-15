---
id: TASK-627
parent: STORY-053
feature: FEATURE-014
status: todo
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

- [ ] A migration with an unterminated `CreateIndex` / `CreateCollection` chain fails, naming it, and is not recorded
- [ ] A terminated chain behaves exactly as today, on every provider
- [ ] Red on the current code, measured on SQLite and at least one document store

## Human test plan

N/A: asserted by the migration record.
