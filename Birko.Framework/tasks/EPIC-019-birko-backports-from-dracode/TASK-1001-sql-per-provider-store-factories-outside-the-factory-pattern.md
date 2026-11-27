---
id: TASK-1001
parent: EPIC-019
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-988, TASK-990, TASK-995]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The older per-provider SQL store factories sit outside the store factory pattern

## Context

Raised by the user (2026-11-27) while comparing the store factory with the event store family ([[TASK-995]]). The store
factory follows the same shape as the event store:
- a common interface: `IStoreFactory`, extended by `ISchemaStoreFactory`, extended by `ISqlStoreFactory`;
- an abstract base: `SqlStoreFactory<TConnector>` for SQL, `SettingsStoreFactory<TSettings>` for everything else;
- a sealed class per provider (`SqLiteSqlStoreFactory`, …), chosen by configuration.

Before that pattern ([[TASK-988]]), each SQL provider already had its own factory, and TASK-988 kept them ("the
per-provider factories stay", `Birko.Data.SQL/CLAUDE.md`). So each provider now carries two factories with near-identical
names:

| Provider | Older factory (outside the pattern) | TASK-988 factory (in it) |
|---|---|---|
| SQLite | `ISqLiteStoreFactory`, `SqLiteStoreFactory`, `SqLiteStoreFactoryOptions`, `AddSqLiteStores` | `SqLiteSqlStoreFactory` |
| PostgreSQL | `IPostgreSQLStoreFactory`, `PostgreSQLStoreFactory`, `…Options`, `AddPostgreSQLStores` | `PostgreSQLSqlStoreFactory` |
| MySQL | `IMySQLStoreFactory`, `MySQLStoreFactory`, `…Options`, `AddMySQLStores` | `MySQLSqlStoreFactory` |
| SQL Server | `IMSSqlStoreFactory`, `MSSqlStoreFactory`, `…Options`, `AddMSSqlStores` | `MSSqlSqlStoreFactory` |

The older ones implement none of the shared interfaces and hand out sync stores (`GetStore<T>()` returning a
`SQLiteStore<T>`, …), each with its own options class and DI extension.

**Measured usage, 2026-11-27:**
- **Consumers:** no `.cs` file under `Birko/Consumers` names an older factory or its `Add*Stores` extension. Symbio,
  DraCode, Sandbox and the others were searched.
- **Framework:** only the four providers' own `Extensions/*ServiceCollectionExtensions.cs`, and about 45 test files.
  Most of those are in `Birko.Data.SQL.SqLite.Tests`, plus the SQL Views, Caching, Sync.Sql, Tenant and
  `SQL.Providers` tests, which mostly use the factory to get a store over a file.

**What only the older SQLite factory is known to do** (`SqLiteStoreFactoryTests`):
- creates a missing database directory eagerly;
- resolves a relative `Location` against `BaseDirectory`;
- returns sync stores;
- registers a singleton through `AddSqLiteStores`.

Whether `SqLiteSqlStoreFactory` does the first two is not checked yet. It makes paths absolute and keeps WAL (TASK-988).
The other three providers' older factories are not surveyed.

## Open decisions (settle with the user before building)

- **Fate of the older factories:**
  - (a) mark them `[Obsolete]`, pointing to `SqlStoreFactory.Create` / the `*SqlStoreFactory`, and keep them working;
  - (b) rebuild them on `SqlStoreFactory<TConnector>` so they become `ISqlStoreFactory` too, keeping their names and
    options as a thin layer;
  - (c) delete them and their `Add*Stores` extensions, moving the tests to the TASK-988 factories (no consumer uses them).
- **What the TASK-988 factories should take over** from the older ones before (a) or (c): eager directory creation,
  `BaseDirectory` resolution, sync stores (`ISqlStoreFactory` hands out async stores only), DI registration.
- **Naming:** whether the surviving per-provider factory keeps the `…SqlStoreFactory` name or takes the plain one.

## Acceptance criteria

- [ ] Decisions recorded.
- [ ] Each provider has one store factory, or the second one is visibly obsolete and points to the first.
- [ ] Nothing the older factories did is lost without a decision, each with a test: directory creation, `BaseDirectory`,
      sync stores, DI registration.
- [ ] Every SQL test project and the consumer aggregators (Symbio.Birko, DraCode.Birko, Sandbox) build; the affected
      suites pass; docs updated (`Birko.Data.SQL/CLAUDE.md`'s "the per-provider factories stay", the provider READMEs,
      the SQLite test docs). The `backend-store-implementations` spec changes at the next `/specs regen`.

## Out of scope

- The non-SQL factories, which are already in the pattern (`SettingsStoreFactory<TSettings>`).
- Making `SqlStoreFactory<TConnector>` derive from `SettingsStoreFactory<TSettings>`: the two bases are separate on
  purpose, mirroring the separate SQL store hierarchy.

## Human test plan

N/A — fully covered by automated tests.
