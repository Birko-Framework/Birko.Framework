---
id: TASK-988
parent: EPIC-019
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A provider-neutral SQL store factory: one interface, the provider chosen by configuration

## Context

Asked for by DraCode (its TASK-200, 2026-11-25; DraCode's user decided it belongs in Birko). DraCode wants its stores to
run on any SQL database, SQLite for tests and PostgreSQL in production, chosen by configuration. Birko has one factory
per provider (`ISqLiteStoreFactory`, `IPostgreSQLStoreFactory`, `IMySQLStoreFactory`, `IMSSqlStoreFactory`), each
returning its own concrete store type, and nothing shared, so DraCode hard-codes `AsyncSqLiteModelRepository` and
`SqliteDatabaseSettings.For` in 38 places. The existing factories also hand out sync stores (`SQLiteStore<T>`), while
DraCode uses async ones.

What DraCode uses from a store today: `CreateSchemaAsync`, the connector's `EnsureColumns`, CRUD / Read / Count,
`AggregateAsync`, bulk operations, and an `AbstractAsyncBulkRepository<T>` over the store.

## Acceptance criteria

- [x] One provider-neutral interface hands out async bulk stores for a model type and the shared connector (for schema
      work), without the caller naming a provider; a provider-neutral way to get a repository over such a store.
- [x] One registration picks the provider from configuration: a provider name (`SqLite` | `PostgreSQL` at least; MySQL
      and SQL Server if they fit the same shape) and a connection string; for SQLite a file path is enough.
- [x] The SQLite path keeps today's `SqLiteSettings` behaviour: WAL on first use and one connector per file (TASK-941;
      DraCode's `SqliteJournalModeTests` guard it).
- [x] An unknown provider name or a connection string the provider cannot use is refused at registration, by name.
- [x] Tests: the same store-level suite runs on SQLite and, live, on PostgreSQL (the `BIRKO_PG_HOST` convention, skipped
      when unset, required under `BIRKO_REQUIRE_LIVE`).
- [x] README / CLAUDE.md for the projects touched; Recent Updates entry.

## Open decisions (settle before building)

- Where it lives: `Birko.Data.SQL` (shared, but it must not reference the provider projects) with each provider
  registering itself, versus a small aggregating project that references them all.
- The connection string vs Birko's settings chain (`SqlSettings` descendants): parse one into the other, or accept both.
- Whether the existing per-provider factories are kept, implemented on top of the new one, or retired.

Inputs from DraCode (2026-11-25), not decisions: it needs one database per process, chosen by a provider name plus a
connection string (or a SQLite file path), so anything in the `SqlSettings` chain buildable from those two strings
works for it; it uses only async stores and repositories, so the sync factories do not matter to it. The user
(2026-11-25): SQL databases only; NoSQL stores already share `IAsyncBulkStore<T>`, so the factory hands stores out as
that and keeps the SQL-only connector / schema part separate.

## Decisions (the user, 2026-11-25: "start 988", taking the recommendations)

- It lives in `Birko.Data.SQL`, which references no provider: each provider registers itself (`AddSqLite()`, …) on
  `SqlStoreProviders`. `Birko.Data.SQL` also stays free of DI (several of its importers do not reference
  `Microsoft.Extensions.DependencyInjection`), so the app registers the factory itself in one line.
- Configuration is a provider name plus a connection string (a file path for SQLite); each provider's
  `FromConnectionString` builds its own settings, and its factory also takes a settings object directly.
- The four per-provider factories stay unchanged.

## Evidence

`Birko.Data.SQL`: `ISqlStoreFactory`, `SqlStoreFactory.Create` / `RequireOnlyKeys`, `SqlStoreProviders`, the shared
`SqlStoreFactory<TConnector>` base (connector, repository, `CreateSchemaAsync`), `IAsyncSqlBulkStore<T>` (implemented by
`AsyncDataBaseBulkStore`) and `AsyncSqlModelRepository<T>`. Each provider: `<P>SqlStoreFactory` with
`FromConnectionString` and `Add<P>()`.
- `SqlStoreFactoryTests` (21, in `Birko.Data.SQL.Providers.Tests`, which now also imports SQLite): unknown provider
  refused naming the registered ones; empty connection string and double registration refused; names ignore case;
  SQLite from a file path runs the store suite (schema, create, bulk create, read, count, repository on the shared
  connector, Sum aggregate, `EnsureColumns` clean) and creates the folder; WAL after first use; absolute, relative and
  `Data Source=` spellings of one file give one connector; an unknown SQLite key refused; PostgreSQL, MySQL and SQL Server
  read every key their settings carry (a quoted password with `;` and `=` included) and refuse pooling, a search path,
  an uncarried SSL / Encrypt mode, integrated security, a named instance, a missing host.
- **Not run here: the live PostgreSQL case** (`PostgreSQL_live_runs_the_same_store_suite`), which skipped because Docker
  was not running on this machine (no `BIRKO_PG_HOST`). It runs in CI's live-tests job and in DraCode's human test.
- `Birko.Data.SQL.Providers.Tests` 61/61; `Birko.Data.SQL.Tests` 1147, SqLite 3193, PostgreSQL 2605, MySQL 2627, MSSql
  2661, `Birko.Data.Tests` 335, all passing; `Birko.Framework.slnx` builds, 0 errors. READMEs (SQL and the four
  providers), Birko.Data.SQL CLAUDE.md, § Recent Updates (oldest entry rolled to CHANGELOG.md).

**Live PostgreSQL passed in CI 2026-11-25:** the live-tests job on Framework a6fd43a5 ran `Birko.Data.SQL.Providers.Tests`
61/61 with none skipped (`BIRKO_PG_HOST` set), so `PostgreSQL_live_runs_the_same_store_suite` ran and passed. That
job failed only on an unrelated Cosmos DB test, failing since an earlier run. DraCode's human test remains.

## Out of scope

- Non-SQL stores (MongoDB, Elasticsearch, …).
- DraCode switching its 38 call sites (DraCode TASK-200).

## Human test plan

- [ ] In DraCode, with its configuration switched to PostgreSQL (a local PostgreSQL in Docker), its stores create their
      schema and read and write; switched back to SQLite, the same; the SQLite file is in WAL mode.
