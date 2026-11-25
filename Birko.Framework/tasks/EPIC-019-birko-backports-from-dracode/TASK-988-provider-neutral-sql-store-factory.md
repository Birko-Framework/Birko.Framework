---
id: TASK-988
parent: EPIC-019
feature: null
status: todo
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

- [ ] One provider-neutral interface hands out async bulk stores for a model type and the shared connector (for schema
      work), without the caller naming a provider; a provider-neutral way to get a repository over such a store.
- [ ] One registration picks the provider from configuration: a provider name (`SqLite` | `PostgreSQL` at least; MySQL
      and SQL Server if they fit the same shape) and a connection string; for SQLite a file path is enough.
- [ ] The SQLite path keeps today's `SqLiteSettings` behaviour: WAL on first use and one connector per file (TASK-941;
      DraCode's `SqliteJournalModeTests` guard it).
- [ ] An unknown provider name or a connection string the provider cannot use is refused at registration, by name.
- [ ] Tests: the same store-level suite runs on SQLite and, live, on PostgreSQL (the `BIRKO_PG_HOST` convention, skipped
      when unset, required under `BIRKO_REQUIRE_LIVE`).
- [ ] README / CLAUDE.md for the projects touched; Recent Updates entry.

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

## Out of scope

- Non-SQL stores (MongoDB, Elasticsearch, …).
- DraCode switching its 38 call sites (DraCode TASK-200).

## Human test plan

- [ ] In DraCode, with its configuration switched to PostgreSQL (a local PostgreSQL in Docker), its stores create their
      schema and read and write; switched back to SQLite, the same; the SQLite file is in WAL mode.
