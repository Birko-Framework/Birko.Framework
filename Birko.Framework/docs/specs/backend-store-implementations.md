---
area: backend-store-implementations
generated-at: fc4c807731169e50b7c842f593690ca7f53e0a1a
generated-on: 2026-11-07
sources:
  - ../Birko.Data.CosmosDB/Repositories/AsyncCosmosDBModelRepository.cs
  - ../Birko.Data.CosmosDB/Repositories/CosmosDBModelRepository.cs
  - ../Birko.Data.CosmosDB/Stores/AsyncCosmosDBStore.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosDBStore.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosTransactionScopeException.cs
  - ../Birko.Data.CosmosDB/Stores/Settings.cs
  - ../Birko.Data.ElasticSearch/Repositories/AsyncElasticSearchModelRepository.cs
  - ../Birko.Data.ElasticSearch/Repositories/AsyncElasticSearchRepository.cs
  - ../Birko.Data.ElasticSearch/Repositories/ElasticSearchModelRepository.cs
  - ../Birko.Data.ElasticSearch/Repositories/ElasticSearchRepository.cs
  - ../Birko.Data.ElasticSearch/Stores/AsyncElasticSearchStore.cs
  - ../Birko.Data.ElasticSearch/Stores/ElasticSearchStore.cs
  - ../Birko.Data.ElasticSearch/Stores/ElasticSearchStoreHelper.cs
  - ../Birko.Data.ElasticSearch/Stores/MappingDrift.cs
  - ../Birko.Data.ElasticSearch/Stores/MappingRepairPlan.cs
  - ../Birko.Data.ElasticSearch/Stores/Settings.cs
  - ../Birko.Data.InfluxDB/Repositories/AsyncInfluxDBModelRepository.cs
  - ../Birko.Data.InfluxDB/Repositories/AsyncInfluxDBRepository.cs
  - ../Birko.Data.InfluxDB/Repositories/InfluxDBModelRepository.cs
  - ../Birko.Data.InfluxDB/Repositories/InfluxDBRepository.cs
  - ../Birko.Data.InfluxDB/Stores/AsyncInfluxDBStore.cs
  - ../Birko.Data.InfluxDB/Stores/InfluxDBStore.cs
  - ../Birko.Data.InfluxDB/Stores/InfluxReadPlan.cs
  - ../Birko.Data.InfluxDB/Stores/Settings.cs
  - ../Birko.Data.JSON/Repositories/AsyncJsonModelRepository.cs
  - ../Birko.Data.JSON/Repositories/AsyncJsonRepository.cs
  - ../Birko.Data.JSON/Repositories/JsonModelRepository.cs
  - ../Birko.Data.JSON/Repositories/JsonRepository.cs
  - ../Birko.Data.JSON/Stores/AbstractAsyncJsonStore.cs
  - ../Birko.Data.JSON/Stores/AbstractJsonStore.cs
  - ../Birko.Data.JSON/Stores/AsyncJsonBatchBulkStore.cs
  - ../Birko.Data.JSON/Stores/AsyncJsonBatchStore.cs
  - ../Birko.Data.JSON/Stores/AsyncJsonSeparateBulkStore.cs
  - ../Birko.Data.JSON/Stores/AsyncJsonSeparateStore.cs
  - ../Birko.Data.JSON/Stores/AsyncJsonStore.cs
  - ../Birko.Data.JSON/Stores/BatchSettings.cs
  - ../Birko.Data.JSON/Stores/JsonBatchBulkStore.cs
  - ../Birko.Data.JSON/Stores/JsonBatchStore.cs
  - ../Birko.Data.JSON/Stores/JsonFileNaming.cs
  - ../Birko.Data.JSON/Stores/JsonSeparateBulkStore.cs
  - ../Birko.Data.JSON/Stores/JsonSeparateStore.cs
  - ../Birko.Data.JSON/Stores/JsonStore.cs
  - ../Birko.Data.JSON/Stores/StoredKeyGuard.cs
  - ../Birko.Data.MongoDB/Repositories/AsyncMongoDBModelRepository.cs
  - ../Birko.Data.MongoDB/Repositories/AsyncMongoDBRepository.cs
  - ../Birko.Data.MongoDB/Repositories/MongoDBModelRepository.cs
  - ../Birko.Data.MongoDB/Repositories/MongoDBRepository.cs
  - ../Birko.Data.MongoDB/Stores/AsyncMongoDBStore.cs
  - ../Birko.Data.MongoDB/Stores/IMongoConnectedStore.cs
  - ../Birko.Data.MongoDB/Stores/MongoDBStore.cs
  - ../Birko.Data.MongoDB/Stores/MongoPropertyUpdateTranslator.cs
  - ../Birko.Data.MongoDB/Stores/Settings.cs
  - ../Birko.Data.RavenDB/Repositories/AsyncRavenDBModelRepository.cs
  - ../Birko.Data.RavenDB/Repositories/AsyncRavenDBRepository.cs
  - ../Birko.Data.RavenDB/Repositories/RavenDBModelRepository.cs
  - ../Birko.Data.RavenDB/Repositories/RavenDBRepository.cs
  - ../Birko.Data.RavenDB/Stores/AsyncRavenDBStore.cs
  - ../Birko.Data.RavenDB/Stores/IRavenConnectedStore.cs
  - ../Birko.Data.RavenDB/Stores/RavenAmbientKey.cs
  - ../Birko.Data.RavenDB/Stores/RavenDBStore.cs
  - ../Birko.Data.RavenDB/Stores/RavenDocumentId.cs
  - ../Birko.Data.RavenDB/Stores/RavenIndexWait.cs
  - ../Birko.Data.RavenDB/Stores/RavenTrackedCopy.cs
  - ../Birko.Data.RavenDB/Stores/Settings.cs
  - ../Birko.Data.SQL.MSSql/Repositories/AsyncMSSqlModelRepository.cs
  - ../Birko.Data.SQL.MSSql/Repositories/AsyncMSSqlRepository.cs
  - ../Birko.Data.SQL.MSSql/Repositories/MSSqlModelRepository.cs
  - ../Birko.Data.SQL.MSSql/Repositories/MSSqlRepository.cs
  - ../Birko.Data.SQL.MSSql/Stores/AsyncMSSqlStore.cs
  - ../Birko.Data.SQL.MSSql/Stores/IMSSqlStoreFactory.cs
  - ../Birko.Data.SQL.MSSql/Stores/MSSqlSettings.cs
  - ../Birko.Data.SQL.MSSql/Stores/MSSqlStore.cs
  - ../Birko.Data.SQL.MSSql/Stores/MSSqlStoreFactory.cs
  - ../Birko.Data.SQL.MSSql/Stores/MSSqlStoreFactoryOptions.cs
  - ../Birko.Data.SQL.MySQL/Repositories/AsyncMySQLModelRepository.cs
  - ../Birko.Data.SQL.MySQL/Repositories/AsyncMySQLRepository.cs
  - ../Birko.Data.SQL.MySQL/Repositories/MySQLModelRepository.cs
  - ../Birko.Data.SQL.MySQL/Repositories/MySQLRepository.cs
  - ../Birko.Data.SQL.MySQL/Stores/AsyncMySQLStore.cs
  - ../Birko.Data.SQL.MySQL/Stores/IMySQLStoreFactory.cs
  - ../Birko.Data.SQL.MySQL/Stores/MySQLStore.cs
  - ../Birko.Data.SQL.MySQL/Stores/MySQLStoreFactory.cs
  - ../Birko.Data.SQL.MySQL/Stores/MySQLStoreFactoryOptions.cs
  - ../Birko.Data.SQL.MySQL/Stores/MySqlSettings.cs
  - ../Birko.Data.SQL.PostgreSQL/Repositories/AsyncPostgreSQLModelRepository.cs
  - ../Birko.Data.SQL.PostgreSQL/Repositories/AsyncPostgreSQLRepository.cs
  - ../Birko.Data.SQL.PostgreSQL/Repositories/PostgreSQLModelRepository.cs
  - ../Birko.Data.SQL.PostgreSQL/Repositories/PostgreSQLRepository.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/AsyncPostgreSQLStore.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/IPostgreSQLStoreFactory.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/PostgreSQLStore.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/PostgreSQLStoreFactory.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/PostgreSQLStoreFactoryOptions.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/PostgreSqlSettings.cs
  - ../Birko.Data.SQL.SqLite/Repositories/AsyncSqLiteModelRepository.cs
  - ../Birko.Data.SQL.SqLite/Repositories/AsyncSqLiteRepository.cs
  - ../Birko.Data.SQL.SqLite/Repositories/SqLiteModelRepository.cs
  - ../Birko.Data.SQL.SqLite/Repositories/SqLiteRepository.cs
  - ../Birko.Data.SQL.SqLite/Stores/AsyncSQLiteStore.cs
  - ../Birko.Data.SQL.SqLite/Stores/ISqLiteStoreFactory.cs
  - ../Birko.Data.SQL.SqLite/Stores/SQLiteStore.cs
  - ../Birko.Data.SQL.SqLite/Stores/SqLiteSettings.cs
  - ../Birko.Data.SQL.SqLite/Stores/SqLiteStoreFactory.cs
  - ../Birko.Data.SQL.SqLite/Stores/SqLiteStoreFactoryOptions.cs
  - ../Birko.Data.TimescaleDB/Repositories/AsyncTimescaleDBModelRepository.cs
  - ../Birko.Data.TimescaleDB/Repositories/TimescaleDBModelRepository.cs
  - ../Birko.Data.TimescaleDB/Stores/AsyncTimescaleDBStore.cs
  - ../Birko.Data.TimescaleDB/Stores/Settings.cs
  - ../Birko.Data.TimescaleDB/Stores/TimescaleDBStore.cs
  - ../Birko.Data.XML/Repositories/AsyncXmlModelRepository.cs
  - ../Birko.Data.XML/Repositories/AsyncXmlRepository.cs
  - ../Birko.Data.XML/Repositories/XmlModelRepository.cs
  - ../Birko.Data.XML/Repositories/XmlRepository.cs
  - ../Birko.Data.XML/Stores/AbstractAsyncXmlStore.cs
  - ../Birko.Data.XML/Stores/AbstractXmlStore.cs
  - ../Birko.Data.XML/Stores/AsyncXmlBatchBulkStore.cs
  - ../Birko.Data.XML/Stores/AsyncXmlBatchStore.cs
  - ../Birko.Data.XML/Stores/AsyncXmlSeparateBulkStore.cs
  - ../Birko.Data.XML/Stores/AsyncXmlSeparateStore.cs
  - ../Birko.Data.XML/Stores/AsyncXmlStore.cs
  - ../Birko.Data.XML/Stores/BatchSettings.cs
  - ../Birko.Data.XML/Stores/StoredKeyGuard.cs
  - ../Birko.Data.XML/Stores/XmlBatchBulkStore.cs
  - ../Birko.Data.XML/Stores/XmlBatchStore.cs
  - ../Birko.Data.XML/Stores/XmlSeparateBulkStore.cs
  - ../Birko.Data.XML/Stores/XmlSeparateStore.cs
  - ../Birko.Data.XML/Stores/XmlStore.cs
# source-commits: omitted — every source is inside this repo, so `generated-at` covers them (regen step 5c).
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-21 (TASK-696) over 376 feature-linked tasks: 117 resolved, 259 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 1 commit leads with the id of a task the state gate refuses. Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 259
---
# Backend store implementations and where they diverge from the store contract

## Purpose

Each persistence backend (SQLite, PostgreSQL, MySQL, SQL Server, TimescaleDB, MongoDB, RavenDB, Cosmos DB,
Elasticsearch, InfluxDB, JSON, XML) ships a sync and an async store that override the `*Core` seams of the abstract
store bases, and a set of thin repositories over them. This spec sets the backends side by side: what each one does
natively (filtered writes, PropertyUpdate translation, sessions and batches, realtime reads, ID mapping) and, above
all, where each one departs from the shared contract in `store-crud-contract.md`. Code that swaps one backend for
another, or tests that run one contract across all of them, depend on knowing these differences.

## Requirements

### Requirement: Backends declare different optional capabilities

The system SHALL give the backends these optional surfaces, and no others:
- transactional context (`ITransactionalStore` / `IAsyncTransactionalStore`): MongoDB (`IClientSessionHandle`, sync and
  async), RavenDB (document session, sync and async), Cosmos DB **async only** (`TransactionalBatch`), and the SQL stores
  through their own connection boundary. Elasticsearch, InfluxDB, JSON, XML and the sync Cosmos DB store have none.
- realtime read by id (`IRealtimeReadStore` / `IAsyncRealtimeReadStore`): Elasticsearch and RavenDB only.
- compare-and-set `UpdateIfAsync`: the async Cosmos DB, RavenDB and Elasticsearch stores only; none of their sync twins.
- MongoDB and RavenDB also pick up an ambient session published for their database (`AmbientSession<T>.Find(AmbientKey)`)
  when no explicit `TransactionContext` is set.

#### Scenario: A sync Cosmos DB store cannot join a unit of work

- **Given** a `CosmosDBStore<T>` (sync)
- **When** a caller looks for `ITransactionalStore<T, …>` on it
- **Then** it is not implemented; only `AsyncCosmosDBStore<T>` implements `IAsyncTransactionalStore<T, TransactionalBatch>`

#### Scenario: A Mongo store inside an ambient session

- **Given** an `AsyncMongoDBStore<T>` with no `TransactionContext`, and an ambient session published under its settings id
- **When** any CRUD or filtered write runs
- **Then** it runs on that ambient session

### Requirement: An unconfigured store refuses every operation, reads included

The system SHALL, on every backend but the in-memory one, refuse every public operation of a store that has no client
because settings were never applied — create, update, delete, save, read and count — with `InvalidOperationException`
naming `SetSettings`. The refusal is in `InitCore(Async)`, which every public operation reaches first (public overrides
that answered without it — the sync JSON and XML `Read(Guid)`, MongoDB's `SaveAsync` and `ReadAllAsync` — now open the
gate). Decided 2026-11-04 (the user) and measured by `tests/Shared/StoreContract/UnconfiguredContract.cs` on every
provider: MongoDB, RavenDB, Cosmos DB and InfluxDB answered reads with `null` / empty / `0`, returned `Guid.Empty` from
`Create` and returned silently from `Update` and `Delete`; JSON and XML refused writes but answered reads empty, which
TASK-464 had kept on purpose (TASK-812 reverses it: an empty answer from a store that cannot look is as false as a write
reported done). The SQL stores (TASK-170) and Elasticsearch already refused. `SetSettings(ISettings)` with a foreign type
throws `ArgumentException` on Elasticsearch, JSON and XML; MongoDB, RavenDB, Cosmos DB and InfluxDB ignore it, and the
store then refuses as unconfigured.

#### Scenario: Update on an unconfigured MongoDB store (TASK-812)

- **Given** an `AsyncMongoDBStore<T>` whose `SetSettings` was never called
- **When** `UpdateAsync(entity)` or `ReadAsync(guid)` is called
- **Then** `InvalidOperationException` names `SetSettings` (it returned without writing, or answered `null`)

#### Scenario: Wrong settings type on Elasticsearch

- **Given** an `AsyncElasticSearchStore<T>`
- **When** `SetSettings` receives a non-Elasticsearch `ISettings`
- **Then** `ArgumentException` is thrown (`AsyncElasticSearchStore.cs:56`)

### Requirement: Backend settings refuse or encode what the connection cannot carry as written

The system SHALL, in RavenDB's `Settings.CreateDocumentStore`, throw `NotSupportedException` when a user name or password
is set, since RavenDB authenticates with a client certificate (TASK-724: they were accepted and never used). MongoDB's
composed `Settings.GetConnectionString` SHALL percent-encode the user name, password, database name, `authSource` and
`replicaSet` (`Uri.EscapeDataString`), leaving `Location` as written because it may be a host list; a set
`RawConnectionString` is returned unchanged (TASK-707: a password `p@ss/w?rd` made the server `ss` and the database `w`).

#### Scenario: A RavenDB settings object carrying a password (TASK-724)

- **Given** a RavenDB `Settings` with `Password` set
- **When** `CreateDocumentStore()` is called
- **Then** `NotSupportedException` is thrown and no `DocumentStore` is created

#### Scenario: A MongoDB password holding URI delimiters (TASK-707)

- **Given** a MongoDB `Settings` with user `u` and password `p@ss/w?rd`
- **When** `GetConnectionString()` is called
- **Then** the credentials read `u:p%40ss%2Fw%3Frd@` before the host

### Requirement: Read by identifier takes a backend fast path that may skip the lazy-initialization gate

The system SHALL implement `Read(Guid)` / `ReadAsync(Guid)` per backend as follows:
- Cosmos DB: a point read with the Guid as id and partition key, after `EnsureInitialized(Async)`; a 404 returns `null`.
- RavenDB: a load by document id `{collection}/{guid}` (`RavenDocumentId.For`), after the gate, on the active session
  if there is one.
- MongoDB, Elasticsearch and the SQL stores: no override; the base filter read.
- InfluxDB: a public override that opens the initialization and cancellation gate and then runs a Flux query; a
  failure other than a cancellation is wrapped in `InvalidOperationException` (TASK-720: it skipped the gate and wrapped
  a cancellation too, measured).
- JSON and XML (sync): a public override answering from the in-memory dictionary, after the gate (TASK-812: it skipped
  it, so an unconfigured store answered `null` and a configured one was read before its data loaded).

#### Scenario: InfluxDB read as the first operation (TASK-720)

- **Given** a configured `AsyncInfluxDBStore<T>` (or `InfluxDBStore<T>`) that has never been initialized
- **When** `ReadAsync(guid)` is the first call, or is called with a cancelled token
- **Then** `InitCoreAsync` runs before the query, and a cancelled token throws `OperationCanceledException`

#### Scenario: Cosmos DB point read of a missing item

- **Given** an `AsyncCosmosDBStore<T>`
- **When** `ReadAsync(guid)` is called for an id with no item
- **Then** `null` is returned (the 404 is caught)

### Requirement: Filtered reads lag writes on Elasticsearch; RavenDB writes wait for their indexes

The system SHALL answer filtered reads and counts on Elasticsearch from the search index, with no refresh requested by
any single-item or bulk write, so a write is visible to them only after the index refreshes. `ReadRealtime(Async)(guid)`
SHALL use a realtime GET (`.Realtime()`), set `Guid` from the id when the source lacks it, return `null` on 404 and throw
`InvalidOperationException` on any other failure. On RavenDB, filtered reads and counts SHALL be `session.Query<T>()`
(index-backed, no wait for non-stale results), while `ReadRealtime(Async)` is the load by id. Every session a RavenDB
store opens itself, sync and async, SHALL go through `RavenIndexWait.Apply`, which sets
`WaitForIndexesAfterSaveChanges(RavenIndexWait.Timeout = 15 s, throwOnTimeout: false)`: a write returns only once the
indexes include it, so a filtered read or count after it sees it, as on the other providers. A wait that times out SHALL
return without throwing (the write is applied; a query may still briefly miss it). A session the caller supplies
(`TransactionContext`, or an ambient one) SHALL be used as given; the store applies no wait to it. Decided 2026-11-07 by
the user (TASK-828: under suite load an update applied to all 6 documents while a query on the value it wrote returned
5, measured); pinned by `tests/Birko.Data.RavenDB.Tests/Stores/Task828IndexWaitTests.cs`.

#### Scenario: Elasticsearch create then read by filter

- **Given** an Elasticsearch store with the default refresh interval
- **When** an entity is created and immediately read through `Read(x => x.Guid == id)` or the base `Read(Guid)`
- **Then** it may not be found until the next refresh, while `ReadRealtimeAsync(id)` returns it

#### Scenario: RavenDB realtime read

- **Given** an `AsyncRavenDBStore<T>`
- **When** `ReadRealtimeAsync(guid)` is called
- **Then** it delegates to `ReadAsync(guid)` (the load by id, `AsyncRavenDBStore.cs:242`)

#### Scenario: RavenDB filtered read straight after a write (TASK-828)

- **Given** an `AsyncRavenDBStore<T>` (or `RavenDBStore<T>`) with no `TransactionContext`, holding 6 documents
- **When** a filtered update writes `Code = "done"` to all 6 and `ReadAsync(x => x.Code == "done")` runs right after
- **Then** all 6 come back, because the update's session waited for the indexes before returning (a query could return 5)

### Requirement: Duplicate-create refusal is detected per backend, and InfluxDB has none

The system SHALL refuse a create of a key already stored with `StoreUniqueViolationException` on every backend except
InfluxDB, which SHALL accept it and write it as a newer point of that entity, the shape an update has — decided
2026-11-04 by the user, as TimescaleDB's acceptance was (TASK-567): refusing would need a read across the bucket's whole
history before every write, and still could not be atomic, as InfluxDB has no unique constraint or transaction. Pinned by
`tests/Birko.Data.InfluxDB.Tests/InfluxDuplicateCreateTests.cs`. The others refuse by these mechanisms:
- MongoDB: `IsDuplicateKeyException` recognises `MongoWriteException` / `MongoBulkWriteException` with category
  `DuplicateKey`.
- Cosmos DB: `IsDuplicateKeyException` recognises `CosmosException` 409, also inside an `AggregateException`.
- Elasticsearch: a 409 from the create API throws `StoreException.DuplicateCreate` directly; a bulk whose only errors are
  `version_conflict_engine_exception` throws it too.
- RavenDB outside a session: `StoreAsync(data, string.Empty, id)` (empty change vector) turns an existing id into a
  `ConcurrencyException`, rethrown as `DuplicateCreate`.
- RavenDB inside a session: a prior `Advanced.Exists(id)` check, which does not cover a concurrent writer between check
  and commit.
- JSON and XML: `StoredKeyGuard.AddNew`, which also refuses two items with one Guid in the same batch.
- SQL: `DuplicateCreate.IsDuplicate(Connector, ex)` around the bulk seam.

InfluxDB SHALL write a point for a create of an existing Guid without refusing it.

#### Scenario: InfluxDB create of an existing Guid

- **Given** an InfluxDB store holding an entity with Guid G
- **When** `Create` is called with another entity carrying G
- **Then** a new point is written and no exception is thrown (`AsyncInfluxDBStore.cs:218-243`)

#### Scenario: RavenDB create inside a unit of work

- **Given** an `AsyncRavenDBStore<T>` with an active session and a document already stored under G
- **When** `CreateAsync` is called with G
- **Then** `StoreUniqueViolationException` is thrown before anything is stored in the session (`AsyncRavenDBStore.cs:212`)

### Requirement: Bulk create is all-or-nothing only on SQL and the single-file and batch file stores

The system SHALL apply a bulk `Create` with one already-stored key as follows:
- SQL (all four providers and TimescaleDB): inside one transaction (`AtomicallyAsync`), so the whole batch rolls back.
- JSON (all variants) and XML single-file and batch stores: `StoredKeyGuard` refuses the whole batch before anything is
  added or saved.
- XML separate-file store: per item through the single-item `CreateCore`, so items before the duplicate stay written.
- MongoDB: one `InsertMany`, so documents before the duplicate are stored when it throws.
- Elasticsearch: one bulk request; the non-conflicting items are indexed and then `StoreUniqueViolationException` (or
  `InvalidOperationException` for mixed errors) is thrown. A batch over `MaxBulkSize` (10 000) is refused up front with
  `ArgumentException`.
- RavenDB outside a session: one session per 512-item chunk, so chunks saved before the failing chunk stay written.
- Cosmos DB outside a batch: parallel `CreateItemAsync` calls; the others complete and the first failure surfaces.

#### Scenario: XML separate-file bulk create with a duplicate in the middle

- **Given** an `XmlSeparateStore<T>` holding Guid B
- **When** bulk `Create([A, B, C])` runs
- **Then** A is written to its file, B throws `StoreUniqueViolationException`, and C is not written (`XmlSeparateStore.cs:206`)

#### Scenario: RavenDB bulk create spanning chunks

- **Given** 600 new entities where item 550 reuses a stored Guid
- **When** bulk `CreateAsync` runs outside a session
- **Then** the first 512 are saved and the second chunk is refused (`AsyncRavenDBStore.cs:560`)

#### Scenario: SQL bulk create with a duplicate

- **Given** a SQL store holding Guid B
- **When** bulk `Create([A, B])` runs outside a caller's transaction
- **Then** neither row is stored and `StoreUniqueViolationException` is thrown

### Requirement: Bulk writes skip null items

The system SHALL skip `null` elements in bulk Create/Update/Delete on every store: MongoDB, RavenDB, Cosmos DB,
Elasticsearch, InfluxDB, JSON, XML, in-memory and SQL (TASK-714: the SQL stores' native bulk overrides passed every element
through, so a `null` element threw `NullReferenceException`, measured on SQLite).

#### Scenario: SQL bulk create with a null element (TASK-714)

- **Given** an `AsyncSQLiteStore<T>` (likewise PostgreSQL, MySQL, SQL Server, TimescaleDB, sync and async)
- **When** `CreateAsync(new[] { a, null, b })` is called
- **Then** `a` and `b` are stored and nothing is thrown

### Requirement: Each backend detects an update miss from its own signal, with listed exceptions

The system SHALL throw `EntityNotFoundException` for an update of an entity that is not stored, with these departures:
- InfluxDB: `Update` writes a point whatever is stored (an upsert), and an entity with no Guid returns silently rather
  than throwing; bulk update drops Guid-less items silently.
- MongoDB: a miss is reported only when the write is acknowledged; an unacknowledged `ReplaceOne` counts as a hit — an
  unacknowledged write concern returns no count to tell a miss by, so refusing would make such a concern unusable for
  updates (TASK-719, recorded, not changed).
- Cosmos DB inside a transactional batch: the replace is queued and the miss surfaces only when the batch executes; an
  entity with no Guid is reported as `Guid.Empty` when the batch is assembled, as outside one (TASK-719: it was skipped
  silently, measured).

A bulk update holding an entity with no Guid SHALL report it as `Guid.Empty` in the `EntityNotFoundException`, after the
stored ones are written, on every backend but InfluxDB (TASK-719: Elasticsearch and the async JSON and XML bulk updates dropped it
silently, measured by `tests/Shared/StoreContract/WriteEdgeContract.cs`).
- RavenDB: the miss comes from the load it performs before copying the entity onto the tracked document.

#### Scenario: InfluxDB update of an unstored entity

- **Given** an InfluxDB store with no point for Guid G
- **When** `Update(entity with G)` is called
- **Then** a point is written and no exception is thrown

#### Scenario: Elasticsearch bulk update with a Guid-less item (TASK-719)

- **Given** a batch of one stored entity and one entity whose `Guid` is `null`
- **When** bulk `UpdateAsync` runs on Elasticsearch
- **Then** the stored entity is updated, then `EntityNotFoundException` reports `Guid.Empty` (`AsyncElasticSearchStore.cs:310-323`)

#### Scenario: Cosmos DB update of a missing item inside a batch

- **Given** an `AsyncCosmosDBStore<T>` with a `TransactionalBatch` set
- **When** `UpdateAsync` is called for an item that does not exist
- **Then** no exception is thrown at the call; the batch fails when it executes

### Requirement: Elasticsearch single-item update is a partial-document merge

The system SHALL implement Elasticsearch `Update` / `UpdateAsync` with the update API and `.Doc(data)`, which merges
the serialized entity into the stored `_source` rather than replacing it; bulk update uses `UpdateMany` with `.Doc` the
same way. A 404 `document_missing_exception` becomes `EntityNotFoundException`; any other failure is
`InvalidOperationException`.

#### Scenario: Updating a document

- **Given** a stored Elasticsearch document
- **When** `UpdateAsync(entity)` is called
- **Then** an update request with `doc` = the entity is sent for `_id` = the Guid (`AsyncElasticSearchStore.cs:234`)

### Requirement: InfluxDB stores an entity as time-stamped points and reads back the newest

The system SHALL write each Create/Update as a point in measurement `typeof(T).Name`, tagged `Guid`, timestamped
`DateTime.UtcNow`, with public properties as fields (null properties skipped; decimals as double; `DateTime` as an ISO
round-trip string, read back with its kind — a UTC value comes back UTC; TASK-720: it came back local, measured; other
types as `ToString()`). Reads SHALL query `range(start: 0)`, pivot fields per timestamp, and keep the newest row per Guid.
A filter SHALL be evaluated in memory after the query. The bulk read's plan is `InfluxReadPlan`, shared by the sync and
async stores: with a filter, or a sort whose fields do not share one direction, the sort, offset and limit run in memory
after the filter; otherwise they run in Flux (TASK-720: they always ran in Flux, before the filter, and the sort took
the first field's direction for all, measured). A filtered count SHALL read all rows and count in memory. Delete SHALL
use the delete API with predicate `_measurement` + `Guid` over epoch to now + 1 year. Every operation SHALL be retried on
transient failures per `RetryPolicy` and any failure rethrown as `InvalidOperationException`. A failure is transient when
it is a timeout, an `HttpRequestException`, or an `InfluxException` with status 429 or 503 (`Settings.IsTransientException`;
TASK-724: it matched "429", "503" or "unavailable" anywhere in the message text).

#### Scenario: Paging with a filter on InfluxDB (TASK-720)

- **Given** 10 entities of which 5 match a filter
- **When** `ReadAsync(filter, limit: 3)` is called
- **Then** the Flux carries no `limit`, and the first 3 of the 5 matching entities come back

#### Scenario: Updating an InfluxDB entity

- **Given** an entity written at time t1
- **When** it is updated at t2
- **Then** a second point is written at t2 and reads return the t2 values (`AsyncInfluxDBStore.cs:647`)

### Requirement: Deleting a missing item is silent everywhere

The system SHALL make single and bulk `Delete` of an entity that is not stored a no-op on every backend, the rest of a
bulk delete still deleted. Cosmos DB SHALL absorb the item-level 404 (sub-status 1003, a missing container, still
propagates) and Elasticsearch a `not_found` result (TASK-719: both threw — Cosmos DB its raw `CosmosException`,
Elasticsearch `InvalidOperationException` — measured by `WriteEdgeContract`). Every backend SHALL skip an entity whose Guid
is `null` or empty.

#### Scenario: Cosmos DB delete of a missing item (TASK-719)

- **Given** an `AsyncCosmosDBStore<T>` (or `CosmosDBStore<T>`) with no item for Guid G
- **When** `DeleteAsync(entity with G)` is called outside a batch, alone or among stored entities
- **Then** nothing is thrown and the stored entities are deleted

### Requirement: Filtered Update and Delete run natively on SQL, MongoDB and Elasticsearch only

The system SHALL run `Delete(filter)` and `Update(filter, PropertyUpdate<T>)` as:
- MongoDB: public overrides that rewrite `MemoryExtensions.Contains`, run `RequireFilter` and `RequireBoundedFilter`, then
  issue `DeleteMany` / `UpdateMany` with the translated update — bypassing `DeleteCore`, so a subclass's `DeleteCore` is
  not reached. No miss is reported. An empty `PropertyUpdate` returns without writing.
- Elasticsearch: public overrides that run `RequireBoundedFilter`, open the gate, and issue `_delete_by_query` /
  `_update_by_query`; any version conflict throws `StoreConcurrencyException` (with applied and conflict counts), any
  failure or invalid response `StoreException`, a timeout `StoreException`. The query matches the index as of its last
  refresh.
- SQL: native statements inherited from `DataBaseBulkStore` / `AsyncDataBaseBulkStore`.
- RavenDB, Cosmos DB, InfluxDB, JSON and XML: the base implementation (read the matches, then write each through
  `*Core`).

`Update(filter, Action<T>)` SHALL be the base read-modify-save on every backend.

#### Scenario: MongoDB filtered delete

- **Given** a MongoDB store subclass overriding `DeleteCore`
- **When** `DeleteAsync(x => x.Age > 30)` runs
- **Then** one `DeleteMany` is issued and the subclass's `DeleteCore` is not called (`AsyncMongoDBStore.cs:461-477`)

#### Scenario: Elasticsearch update-by-query hits a conflict

- **Given** a document changed between the query snapshot and the write
- **When** `UpdateAsync(filter, updates)` runs
- **Then** `StoreConcurrencyException` reports how many documents were applied before it stopped

### Requirement: PropertyUpdate is translated natively on MongoDB and Elasticsearch

The system SHALL translate each assignment through `Match(set, increment, computed)`:
- MongoDB (`MongoPropertyUpdateTranslator`): a set is `$set` with the value serialized through the member's BSON
  serializer (null as `BsonNull`); an increment is `$inc`, refused with `NotSupportedException` when the member is
  stored with a string representation. An update holding any computed set SHALL instead be one pipeline update (a single
  `$set` stage reading the document as it was before the update): a set is a `$literal`, an increment `$add`, a formula
  `$add` / `$subtract` / `$multiply` / `$divide` over the stored elements and Decimal128 constants, a half-even round
  `$round` and a half-away-from-zero round emulated around `$floor`; the assigned member and every column the formula
  reads are refused with `NotSupportedException` when stored as a string (TASK-826).
- Elasticsearch (`ElasticSearchStoreHelper.BuildUpdateScript`): a Painless script `ctx._source.{storedField} = params.p_X`
  or `+=` for an increment, with a null value written as a literal `null`; a computed set is a `BigDecimal` expression
  (a column parsed from its stored text, a constant a string parameter, division at `MathContext.DECIMAL128`, a round
  `setScale` with `HALF_EVEN` or `HALF_UP`) stored as `.doubleValue()` (TASK-826).

An assignment whose property cannot be resolved SHALL throw `ArgumentException` on both.

#### Scenario: Incrementing a string-stored decimal on MongoDB

- **Given** a decimal member mapped with `BsonRepresentation(BsonType.String)`
- **When** `UpdateAsync(filter, new PropertyUpdate<T>().Increment(x => x.Amount, 1))` runs
- **Then** `NotSupportedException` is thrown before any write (`MongoPropertyUpdateTranslator.cs:157-169`)

#### Scenario: A computed set on MongoDB (TASK-826)

- **Given** a MongoDB store whose `Net` and `Gross` are stored as Decimal128
- **When** `UpdateAsync(filter, new PropertyUpdate<T>().Compute(x => x.Net, x => Math.Round(x.Gross / 1.2m, 2, MidpointRounding.ToEven)))` runs
- **Then** one pipeline update sets `Net` to `$round` of `$divide` of `$Gross` by 1.2, to 2 digits

### Requirement: Async MongoDB, Cosmos DB and RavenDB save natively; their sync twins use the shared routing

The system SHALL implement `SaveAsync` on the async MongoDB, Cosmos DB and RavenDB stores as a native upsert
(`ReplaceOne` with `IsUpsert`, `UpsertItemAsync`, `StoreAsync` without a change vector), overriding the public wrapper.
Each SHALL assign the Guid before the store delegate runs, as the create paths do (TASK-719: Cosmos DB and RavenDB ran
the delegate first, so it saw a null Guid, measured). A unique-index collision on the MongoDB and Cosmos DB upserts SHALL
be `StoreUniqueViolationException`, through the store's `IsDuplicateKeyException`, as on the create path (TASK-719: the
driver's exception propagated, measured on MongoDB; Cosmos DB's unique-key 409 is translated the same way, not run).
The sync MongoDB, Cosmos DB and RavenDB stores, and every other backend, SHALL use the base `StoreSave` routing.
InfluxDB's `SaveAsync` routes to `CreateAsync` / `UpdateAsync`.

#### Scenario: Cosmos DB save of a new entity with a delegate (TASK-719)

- **Given** an entity with `Guid == null` and a delegate that reads `Guid`
- **When** `AsyncCosmosDBStore.SaveAsync(entity, delegate)` (or RavenDB's) runs
- **Then** the delegate sees the Guid the entity is saved under (it saw `null`)

#### Scenario: MongoDB save colliding with a unique index (TASK-719)

- **Given** a unique index on `Name` and an entity carrying a Guid
- **When** `SaveAsync` would store a `Name` another document holds
- **Then** `StoreUniqueViolationException` is thrown (a `MongoWriteException` propagated)

### Requirement: A Cosmos DB transactional batch can only ever cover one entity

The system SHALL partition every Cosmos DB item by its Guid string (id = partition key) and SHALL refuse a
`Settings.PartitionKeyPath` other than `/id` with `NotSupportedException`. Inside a `TransactionalBatch`, the first item
added SHALL fix the batch's partition, and an item with any other Guid SHALL throw `CosmosTransactionScopeException`
(a `UnitOfWorkException`). `UpdateIfAsync` SHALL refuse to run inside a batch.

#### Scenario: Two entities in one Cosmos DB batch

- **Given** an `AsyncCosmosDBStore<T>` with a batch set and entity A already added
- **When** entity B (another Guid) is created in the same batch
- **Then** `CosmosTransactionScopeException` is thrown (`AsyncCosmosDBStore.cs:76-88`)

### Requirement: RavenDB sessions copy onto tracked documents and save only at commit

The system SHALL, on RavenDB, address documents as `{collection}/{guid}`; inside a unit-of-work session store, update
and delete without calling `SaveChanges` (the commit saves); and apply an update by loading the stored document and
copying every public settable property and non-readonly field of the entity onto it (`RavenTrackedCopy.Into`), refusing
with `InvalidOperationException` when the runtime types differ. `UpdateIfAsync` SHALL refuse to run inside a session.

#### Scenario: Updating with a different runtime type

- **Given** a stored document of type `Derived` loaded through a `Base`-typed store
- **When** an update passes an instance whose runtime type is `Base`
- **Then** `InvalidOperationException` names both types

### Requirement: MongoDB aggregation and change streams ignore the session

The system SHALL run `Aggregate()` pipelines, `AggregateAsync` and `WatchAsync` on the collection without the active or
ambient session, so they do not see a transaction's uncommitted writes.

#### Scenario: Aggregating inside a MongoDB transaction

- **Given** an `AsyncMongoDBStore<T>` in a session with an uncommitted insert
- **When** `AggregateAsync(query)` runs
- **Then** the uncommitted document is not counted (`AsyncMongoDBStore.cs:596`)

### Requirement: File stores keep the caller's instances and differ in how they persist

The system SHALL, on the JSON and XML stores, keep entities in a non-concurrent `Dictionary<Guid, T>` holding the
caller's own instances (no copy on write or read), and persist as follows:
- every file a JSON or XML store writes is written through `Birko.Helpers.AtomicFile`: to `.{name}.{random}.tmp` beside
  it, then moved over it, so a failed write leaves the previous file and no temp file (TASK-721; JSON used to delete the
  file first, measured: an empty file after a failed write).
- JSON separate-file: rewrite every entity's file on every write and delete matching files not in memory.
- XML separate-file: write only the touched entity's file.
- XML batch: write every batch, then delete the batch files no longer written (they were deleted first, so a failed
  write lost them all: measured). Atomic per file, not across files.
- JSON and XML batch stores: require `BatchSettings`, refusing plain `Settings` with `InvalidDataException`.
- every JSON and XML separate and batch store, sync and async, keeps its files directly in `Location` (TASK-811: the
  sync `JsonSeparateStore` and `JsonBatchStore` used `Location/Name/` — the single-file store's data-file path — so a
  sync and an async store on one settings did not see each other's records, measured; decided 2026-11-04, one flat
  layout). A file is a store's only when it is its template (`{Name}-*`, `{Name}-*.xml`, or a `Name` holding `*`) with
  exactly one key in place of the `*`: a Guid, or an XML batch number (`Birko.Helpers.RecordFiles`). The bare glob also
  matched `items-archive-{guid}`, so a store named `items` read, rewrote and destroyed another's records (measured on the
  JSON and XML stores).
- `Destroy` on a separate or batch store deletes its own files, then its directory only if nothing else is in it
  (TASK-721: it threw `IOException` when anything else was there, measured).

Every loader SHALL skip a null record (the async JSON, the sync and async single-file XML and the XML and async JSON batch
loaders threw `NullReferenceException`: measured). The async JSON and XML stores SHALL load from disk once (`_loaded`),
again after `Destroy`; the async JSON stores re-read whenever they held no rows (measured: 4 reads for 3 calls).

#### Scenario: Mutating an entity after creating it in a JSON store

- **Given** an entity created in a `JsonStore<T>`
- **When** the caller changes a property without calling `Update`
- **Then** `Read(guid)` returns the changed instance, though the file still holds the old value (`AbstractJsonStore.cs:95`)

#### Scenario: A failed JSON save (TASK-721)

- **Given** a `JsonStore<T>`, `JsonSeparateStore<T>` or `JsonBatchStore<T>` (or an async one) holding a record
- **When** the next save's write throws
- **Then** every file is as it was before the save, and no temp file is left

#### Scenario: Destroy beside a foreign file (TASK-721)

- **Given** a separate or batch store whose directory also holds `keep.txt`
- **When** `Destroy` runs
- **Then** the store's files are deleted, `keep.txt` and the directory stay, and nothing throws

### Requirement: Backend repositories destroy their store twice

The system SHALL, in the MongoDB, InfluxDB, Cosmos DB, RavenDB, SQLite, PostgreSQL, MySQL and SQL Server repositories,
override `DestroyAsync` (and `Destroy` on the sync MongoDB and InfluxDB repositories) to call the base, which destroys
the store, and then destroy or drop the unwrapped store a second time. The TimescaleDB repository no longer does this.

#### Scenario: Destroying a RavenDB repository

- **Given** an `AsyncRavenDBModelRepository<T>`
- **When** `DestroyAsync()` is called
- **Then** the database delete runs once through the base and again on the unwrapped store
  (`AsyncRavenDBModelRepository.cs:69-72`)

### Requirement: SQL store factories hand out async stores, except SQLite

The system SHALL have the PostgreSQL, MySQL and SQL Server factories return the async store from `GetAsyncStore<T>()`,
and the SQLite factory return the sync `SQLiteStore<T>` from `GetStore<T>()`, after creating the database directory.

#### Scenario: Getting a store from the SQLite factory

- **Given** a `SqLiteStoreFactory`
- **When** a store is requested
- **Then** a sync `SQLiteStore<T>` is returned (`SqLiteStoreFactory.cs:47`)
