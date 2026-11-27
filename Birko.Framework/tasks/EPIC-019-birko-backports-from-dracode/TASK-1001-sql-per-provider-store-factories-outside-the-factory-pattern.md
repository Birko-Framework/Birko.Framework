---
id: TASK-1001
parent: EPIC-019
feature: null
status: done
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

## Decisions (the user's, 2026-11-27)

- **(c) delete the older factories**, after checking what only they did ("do the checks and delete the older
  factories"). What went with them is listed under Done; none of it had a consumer.

## Options as filed

- **Fate of the older factories:**
  - (a) mark them `[Obsolete]`, pointing to `SqlStoreFactory.Create` / the `*SqlStoreFactory`, and keep them working;
  - (b) rebuild them on `SqlStoreFactory<TConnector>` so they become `ISqlStoreFactory` too, keeping their names and
    options as a thin layer;
  - (c) delete them and their `Add*Stores` extensions, moving the tests to the TASK-988 factories (no consumer uses them).
- **What the TASK-988 factories should take over** from the older ones before (a) or (c): eager directory creation,
  `BaseDirectory` resolution, sync stores (`ISqlStoreFactory` hands out async stores only), DI registration.
- **Naming:** whether the surviving per-provider factory keeps the `…SqlStoreFactory` name or takes the plain one.

## Acceptance criteria

- [x] Decisions recorded.
- [x] Each provider has one store factory, or the second one is visibly obsolete and points to the first.
- [x] Nothing the older factories did is lost without a decision, each with a test: directory creation, `BaseDirectory`,
      sync stores, DI registration.
- [x] Every SQL test project and the consumer aggregators (Symbio.Birko, DraCode.Birko, Sandbox) build; the affected
      suites pass; docs updated (`Birko.Data.SQL/CLAUDE.md`'s "the per-provider factories stay", the provider READMEs,
      the SQLite test docs). The `backend-store-implementations` spec changes at the next `/specs regen`.

## Done 2026-11-27

**The checks, before deleting:**

| What the older factory did | TASK-988 factory | Outcome |
|---|---|---|
| SQLite: create a missing database directory | `SqLiteSqlStoreFactory` does it too | kept, tested (`SqLiteSqlStoreFactoryTests.Constructor_creates_missing_directory`) |
| SQLite: resolve a relative `Location` against `BaseDirectory` | no; `FromConnectionString` makes a relative path absolute against the working directory | removed; a host combines the path before building the settings |
| SQLite: sync `GetStore<T>()` | no; `ISqlStoreFactory` hands out async stores | removed; construct `SQLiteStore<T>` with the settings |
| PostgreSQL / MySQL / SQL Server: settings, async store, connector | the same, built from the provider's settings or a connection string | kept; the provider tests now check the connector gets the provider's full settings (`UseBinaryImport`, `BulkInsertBatchSize`, MARS) |
| `Add{SqLite,PostgreSql,MySql,MSSql}Stores` DI registration | none: `Birko.Data.SQL` stays free of DI by design (TASK-988) | removed; an app registers the factory itself |
| Options classes (`…StoreFactoryOptions`) | the provider's settings class carries the same values | removed |

No consumer used any of them (measured above).

**Removed:** 16 files: per provider, `I…StoreFactory`, `…StoreFactory` and `…StoreFactoryOptions` in `Stores/`, plus the
DI extension in `Extensions/`, which is now empty. Their `.projitems` entries and the spec map's source lines went too.

**Tests:**
- 48 SQLite fixture call sites (in the SQLite, SQL Views, SqLite.View, Caching, Sync.Sql and Tenant tests) moved to `new SqLiteSqlStoreFactory(new SqLiteSettings(dir, file))`, a
  drop-in: same connector, folder created.
- `SqLiteStoreFactoryTests` became `SqLiteSqlStoreFactoryTests`, covering folder creation and connector and store wiring.
- `ProviderStoreFactoryTests`: the settings tests and the three live round-trips moved to the TASK-988 factories; the
  four DI tests were removed with the extensions.
- The `Microsoft.Extensions.DependencyInjection` package was dropped from the SQLite and Providers test projects, both
  of which build without it.

**Verified:**
- `Birko.Framework.slnx` builds with 0 errors.
- Suites:

  | Suite | Passed |
  |---|---|
  | SQL.SqLite | 3190/3190 |
  | SQL.Providers | 58/58 |
  | SQL.Caching | 43/43 |
  | SQL.Views | 4051/4051 |
  | SQL.SqLite.View | 10/10 |
  | Sync.Sql | 9/9 |
  | Tenant | 94/94 |

- Live, on the local Docker servers (`birko-live-pg`, `-mysql`, `-mssql`) with `BIRKO_REQUIRE_LIVE=1`: SQL.Providers
  58/58, including the PostgreSQL, MySQL and SQL Server round-trips on the TASK-988 factories; SQL.Views 4051/4051
  (3 min 15 s, against 25 s without servers).
- Symbio.Birko, DraCode.Birko and Birko.Sandbox build.
- `spec-links --check` agrees.
- Spec drift: `docs/specs/backend-store-implementations.md` still lists the removed files and a `SqLiteStoreFactory`
  scenario. It is generated, and updates at the next `/specs regen`.

## Out of scope

- The non-SQL factories, which are already in the pattern (`SettingsStoreFactory<TSettings>`).
- Making `SqlStoreFactory<TConnector>` derive from `SettingsStoreFactory<TSettings>`: the two bases are separate on
  purpose, mirroring the separate SQL store hierarchy.

## Human test plan

N/A — fully covered by automated tests.
