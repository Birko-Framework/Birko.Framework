---
id: STORY-039
parent: EPIC-016
status: in-progress
created: 2026-07-06
---

# Cross-provider SQL store-factory + DI backport

## User story

As a **Birko consumer building an ASP.NET service on MSSql / MySQL / PostgreSQL**, I want the same
one-line store-factory + `IServiceCollection` wiring that SQLite gained from Reps, so that I don't
hand-register stores per app just because I picked a server database instead of SQLite.

## Behaviour

- `AddMSSqlStores(...)` / `AddMySqlStores(...)` / `AddPostgreSqlStores(...)` register a store factory
  and the model-mapped stores in DI, mirroring `AddSqLiteStores(...)`.
- The **shape** is copied from SQLite (factory + options + `Extensions/ServiceCollection`), but the
  SQLite-specific content-root path resolution / `Directory.CreateDirectory` is dropped — each
  provider builds its own connection-string settings (`MSSqlSettings` / `MySqlSettings` /
  `PostgreSqlSettings`, which already exist).
- No change to the create-tables migration or the migration-transaction behaviour — the review
  confirmed those are already shared across all SQL providers.

## Backport-review basis

From the 2026-07-06 backend review: only **TASK-033** (SQLite store-factory + DI) is a genuine
cross-provider gap. `Birko.Data.SQL.{MSSql,MySQL,PostgreSQL}` contain `Stores/` only — no factory,
no DI extension, no `Extensions/` folder — whereas `Birko.Data.SQL.SqLite` has both. TASK-032 and
TASK-034 were verified provider-agnostic and need no backport.
