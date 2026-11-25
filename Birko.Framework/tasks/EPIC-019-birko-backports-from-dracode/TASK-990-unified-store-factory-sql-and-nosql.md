---
id: TASK-990
parent: EPIC-019
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: [TASK-988]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A unified store factory: one interface for SQL and NoSQL stores, the backend chosen by configuration

## Context

Asked for by the user (2026-11-25, "add also the nosql and unified store factory"), widening [[TASK-988]], which the user
had scoped to SQL databases. TASK-988 added `ISqlStoreFactory` / `SqlStoreFactory.Create(provider, connectionString,
p => p.AddSqLite().AddPostgreSQL())` for SQLite, PostgreSQL, MySQL and SQL Server. The NoSQL backends have no shared
factory at all: MongoDB, Elasticsearch, RavenDB, Cosmos DB, InfluxDB, and the file and memory stores (JSON, XML,
InMemory), each configured through its own settings type. Every store already implements `IAsyncBulkStore<T>`, so code
that only reads and writes is backend-neutral once it has a store; what is missing is one way to get one.

## Acceptance criteria

- [x] One backend-neutral factory interface (e.g. `IStoreFactory`) hands out `IAsyncBulkStore<T>` stores and an
      `AbstractAsyncBulkRepository<T>` over them, without the caller naming the backend. `ISqlStoreFactory` extends it,
      keeping its SQL-only part (connector, `CreateSchemaAsync`), so TASK-988's callers do not change.
- [x] One `Create(backend, connectionString, providers)` picks the backend by name from those registered; each backend
      project registers itself (`AddMongoDB()`, `AddElasticSearch()`, …), as the SQL providers do; the shared project
      references none of them.
- [x] Each NoSQL backend parses its own connection string into its settings type and refuses, by name, anything its
      settings cannot carry (rule 41), as the SQL providers do.
- [x] What a backend can do beyond CRUD and bulk (aggregation, schema / index creation, views) is reached through
      capability interfaces the caller checks (`store is IAsyncAggregatableStore<T>`), never assumed.
- [ ] Tests: the same store suite runs on InMemory (or JSON) offline and on each live NoSQL backend under its
      `BIRKO_*` variable (skipped when unset, required under `BIRKO_REQUIRE_LIVE`), and on SQLite through the same
      interface.
- [x] READMEs and the projects' CLAUDE.md; Recent Updates entry.

## Open decisions (settle before building)

- Which backends are in: all of MongoDB, Elasticsearch, RavenDB, Cosmos DB, InfluxDB, JSON, XML, InMemory, or the
  document databases first.
- A "connection string" for backends that do not have one in the usual sense (Elasticsearch URLs, Cosmos endpoint + key,
  JSON / XML folders, InMemory with none): the string each accepts.
- Where the shared interface and registry live (`Birko.Data.Stores`, which every backend already references, is the
  candidate), and whether `SqlStoreProviders` becomes the shared registry or stays a SQL view of it.
- Index / schema setup for NoSQL (Elasticsearch mappings, Mongo indexes, Cosmos containers): part of the factory, or
  left to each backend's own API.

## Decisions (the user, 2026-11-25)

- **Backends:** MongoDB, Elasticsearch, RavenDB, Cosmos DB, JSON, XML and InMemory. **InfluxDB waits**: it is a
  time-series store whose point layout is still open (TASK-910).
- **Connection string:** the product's own format where it has one (`mongodb://…`, Cosmos
  `AccountEndpoint=…;AccountKey=…`) plus the keys the settings need beside it; `Key=Value;…` for the rest; InMemory
  takes none. The string is only parsed into the backend's own `Settings` (refusing by name any key it cannot carry,
  rule 41), and the factory is always built from that settings object. **`StoreFactory.Create(settings, providers)`**
  takes a settings object directly and picks the backend from its type (the user asked: "we also have the settings class
  for this"), so a consumer that already builds `Settings` needs no string.
- **Registry:** `Birko.Data.Stores` holds `IStoreFactory`, `StoreProviders` and `StoreFactory`; the SQL providers
  register into it too, and `SqlStoreFactory.Create` stays as the typed SQL entry so TASK-988 callers do not change.
  Repositories: `Birko.Data.Repositories` depends on `Birko.Data.Stores`, not the reverse, so the shared interface
  hands out stores, and a `GetAsyncRepository<T>()` extension in Repositories wraps one in a generic
  `AsyncStoreRepository<T>`. `ISqlStoreFactory.GetAsyncRepository` (an instance method) still wins for SQL callers.
  No new project reference.
- **Schema / indexes:** an optional `ISchemaStoreFactory` capability (`CreateSchemaAsync(types)`) the caller checks
  for; SQL has it, a NoSQL backend has it only where its store can ensure its own schema.

## Evidence

Built 2026-11-25 (one commit with this file).
- **Birko.Data.Stores:** `IStoreFactory` (Backend, Settings, `GetAsyncStore<T>`), `ISchemaStoreFactory`, `StoreProviders`
  (`Add<TSettings>(name, parse, create)`, a name twice refused), `StoreFactory.Create(backend, connectionString, …)` and
  `Create(settings, …)`, `StoreFactory.ParseKeys` (unknown keys refused by name), `SettingsStoreFactory<TSettings>`,
  `FileStoreFolders`. **Birko.Data.Repositories:** `AsyncStoreRepository<T>` and `GetAsyncRepository<T>(this IStoreFactory)`.
- **SQL:** `ISqlStoreFactory : ISchemaStoreFactory` (its `Settings` / `GetAsyncStore` now `new`, the neutral ones
  explicit); each provider also has `AddX(this StoreProviders)`; `SqlStoreFactory.Create` and its callers unchanged
  (Birko.Data.SQL.Providers.Tests 61/61).
- **Backends:** `InMemoryStoreFactory`, `JsonStoreFactory`, `XmlStoreFactory` (a folder; each type its own file named by
  full type name), `MongoDBStoreFactory` (own URI verbatim, database from its path), `ElasticSearchStoreFactory`
  (`Url;IndexPrefix`), `RavenDBStoreFactory` (`Url;Database`), `CosmosDBStoreFactory` (own string + `Database`, optional
  `ConnectionMode`; container per type, a fixed container refused).
- **Found while building (measured):** every non-SQL factory keeps **one store per type**. InMemory, JSON and XML keep
  the data in the store object (two instances do not see each other's writes), and the MongoDB, RavenDB and Cosmos DB
  stores build their own client in `SetSettings` (a `MongoClient`, `DocumentStore`, `CosmosClient` per call otherwise).
  Two defects the tests caught, red first: `MongoServerAddress.ToString()` is the type name, so every MongoDB string got
  one `Location` and therefore one client-cache id; and matching settings by derivation sent a RavenDB `Settings` to
  InMemory (every settings class derives from `Birko.Configuration.Settings`), so those base types now match only
  exactly. A `Birko.Data.StoreFactory` namespace hides the class from code under `Birko.Data.*`, so backend code
  writes it in full; the test project keeps that namespace to hold it.
- **Schema:** SQL only. The NoSQL stores create their database / index / container on first use (MongoDB, RavenDB,
  Elasticsearch seen live) and do not claim `ISchemaStoreFactory`.
- **Tests:** `tests/Birko.Data.StoreFactory.Tests` 26/26: the suite offline on InMemory, JSON, XML, SQLite; backend
  choice and refusals; every backend's parsing; **live** on MongoDB 7, Elasticsearch 7.17.25 and RavenDB (Docker,
  `birko-live-*`). Elasticsearch needs a refresh between write and search (near-real-time), done as the store
  contract's Settle. **Cosmos DB live not run yet** (the emulator is the user's to start). Solution builds; no new
  nullable warnings in the new files.

## Out of scope

- Changing what any store does; only how it is obtained.

## Human test plan

- [ ] In a consumer (DraCode or the Sandbox), switch one store set between SQLite, PostgreSQL and MongoDB by
      configuration alone; reads and writes work on each.
