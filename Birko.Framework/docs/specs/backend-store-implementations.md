---
area: backend-store-implementations
generated-at: 50828c68d6f35ca2096600381c2d0c4481132330
generated-on: 2026-10-21
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

### Requirement: An unconfigured store fails loudly on some backends and silently on others

The system SHALL, when a store has no client because settings were never applied or were of the wrong type:
- Elasticsearch: refuse a non-Elasticsearch `ISettings` in `SetSettings` with `ArgumentException`, and throw
  `InvalidOperationException` from `RequireConnector` on reads and at initialization.
- JSON and XML: throw `InvalidOperationException` from `EnsureWritable` on every write; `SetSettings(ISettings)` with a
  foreign type throws `ArgumentException`.
- MongoDB, RavenDB, Cosmos DB and InfluxDB: ignore a foreign settings type, and then answer reads with `null` / empty /
  `0`, return `Guid.Empty` from `Create`, and return silently from `Update` and `Delete` — no `EntityNotFoundException`.

#### Scenario: Update on an unconfigured MongoDB store

- **Given** an `AsyncMongoDBStore<T>` whose `SetSettings` was never called (`Collection == null`)
- **When** `UpdateAsync(entity)` is called for an entity that is not stored
- **Then** it returns without writing and without throwing (`AsyncMongoDBStore.cs:226`)

#### Scenario: Wrong settings type on Elasticsearch

- **Given** an `AsyncElasticSearchStore<T>`
- **When** `SetSettings` receives a non-Elasticsearch `ISettings`
- **Then** `ArgumentException` is thrown (`AsyncElasticSearchStore.cs:56`)

### Requirement: Read by identifier takes a backend fast path that may skip the lazy-initialization gate

The system SHALL implement `Read(Guid)` / `ReadAsync(Guid)` per backend as follows:
- Cosmos DB: a point read with the Guid as id and partition key, after `EnsureInitialized(Async)`; a 404 returns `null`.
- RavenDB: a load by document id `{collection}/{guid}` (`RavenDocumentId.For`), after the gate, on the active session
  if there is one.
- MongoDB, Elasticsearch and the SQL stores: no override; the base filter read.
- InfluxDB: a public override that runs a Flux query **without** opening the initialization gate or checking the token
  first, and wraps every failure (including cancellation) in `InvalidOperationException`.
- JSON and XML (sync): a public override answering from the in-memory dictionary without opening the gate.

#### Scenario: InfluxDB read as the first operation

- **Given** a configured `AsyncInfluxDBStore<T>` that has never been initialized
- **When** `ReadAsync(guid)` is the first call
- **Then** the bucket-ensure in `InitCoreAsync` does not run before the query (`AsyncInfluxDBStore.cs:99`)

#### Scenario: Cosmos DB point read of a missing item

- **Given** an `AsyncCosmosDBStore<T>`
- **When** `ReadAsync(guid)` is called for an id with no item
- **Then** `null` is returned (the 404 is caught)

### Requirement: Filtered reads on search-indexed backends lag writes; realtime reads do not

The system SHALL answer filtered reads and counts on Elasticsearch from the search index, with no refresh requested by
any single-item or bulk write, so a write is visible to them only after the index refreshes. `ReadRealtime(Async)(guid)`
SHALL use a realtime GET (`.Realtime()`), set `Guid` from the id when the source lacks it, return `null` on 404 and throw
`InvalidOperationException` on any other failure. On RavenDB, filtered reads and counts SHALL be `session.Query<T>()`
(index-backed, no wait for non-stale results), while `ReadRealtime(Async)` is the load by id.

#### Scenario: Elasticsearch create then read by filter

- **Given** an Elasticsearch store with the default refresh interval
- **When** an entity is created and immediately read through `Read(x => x.Guid == id)` or the base `Read(Guid)`
- **Then** it may not be found until the next refresh, while `ReadRealtimeAsync(id)` returns it

#### Scenario: RavenDB realtime read

- **Given** an `AsyncRavenDBStore<T>`
- **When** `ReadRealtimeAsync(guid)` is called
- **Then** it delegates to `ReadAsync(guid)` (the load by id, `AsyncRavenDBStore.cs:238`)

### Requirement: Duplicate-create refusal is detected per backend, and InfluxDB has none

The system SHALL refuse a create of a key already stored with `StoreUniqueViolationException` on every backend except
InfluxDB, by these mechanisms:
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
- **Then** `StoreUniqueViolationException` is thrown before anything is stored in the session (`AsyncRavenDBStore.cs:208`)

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
- **Then** the first 512 are saved and the second chunk is refused (`AsyncRavenDBStore.cs:556`)

#### Scenario: SQL bulk create with a duplicate

- **Given** a SQL store holding Guid B
- **When** bulk `Create([A, B])` runs outside a caller's transaction
- **Then** neither row is stored and `StoreUniqueViolationException` is thrown

### Requirement: Bulk writes skip null items except on SQL

The system SHALL skip `null` elements in bulk Create/Update/Delete on MongoDB, RavenDB, Cosmos DB, Elasticsearch,
InfluxDB, JSON and XML. The SQL stores' native bulk overrides SHALL pass every element through, so a `null` element in a
bulk create throws `NullReferenceException` at `item.EnsureGuid()`.

#### Scenario: SQL bulk create with a null element

- **Given** an `AsyncSQLiteStore<T>` (likewise PostgreSQL, MySQL, SQL Server, TimescaleDB, sync and async)
- **When** `CreateAsync(new[] { a, null })` is called
- **Then** `NullReferenceException` is thrown (`AsyncSQLiteStore.cs:115`)

### Requirement: Each backend detects an update miss from its own signal, with listed exceptions

The system SHALL throw `EntityNotFoundException` for an update of an entity that is not stored, with these departures:
- InfluxDB: `Update` writes a point whatever is stored (an upsert), and an entity with no Guid returns silently rather
  than throwing; bulk update drops Guid-less items silently.
- MongoDB: a miss is reported only when the write is acknowledged; an unacknowledged `ReplaceOne` counts as a hit.
- Cosmos DB inside a transactional batch: the replace is queued and the miss surfaces only when the batch executes;
  bulk update inside a batch skips Guid-less items silently, where outside a batch they are reported as `Guid.Empty`.
- Elasticsearch bulk update, and the async JSON and XML bulk updates: Guid-less items are dropped silently instead of
  being reported as `Guid.Empty` (the sync JSON/XML bulk updates do report them).
- RavenDB: the miss comes from the load it performs before copying the entity onto the tracked document.

#### Scenario: InfluxDB update of an unstored entity

- **Given** an InfluxDB store with no point for Guid G
- **When** `Update(entity with G)` is called
- **Then** a point is written and no exception is thrown

#### Scenario: Elasticsearch bulk update with a Guid-less item

- **Given** a batch of one stored entity and one entity whose `Guid` is `null`
- **When** bulk `UpdateAsync` runs on Elasticsearch
- **Then** the stored entity is updated and no `EntityNotFoundException` is thrown (`AsyncElasticSearchStore.cs:306`)

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
string; other types as `ToString()`). Reads SHALL query `range(start: 0)`, pivot fields per timestamp, and keep the newest
row per Guid. A filter SHALL be evaluated in memory after the query; for the bulk read, ordering, offset and limit are
applied in Flux **before** that in-memory filter. A filtered count SHALL read all rows and count in memory. Delete SHALL
use the delete API with predicate `_measurement` + `Guid` over epoch to now + 1 year. Every operation SHALL be retried on
transient failures per `RetryPolicy` and any failure rethrown as `InvalidOperationException`.

#### Scenario: Paging with a filter on InfluxDB

- **Given** 10 entities of which 5 match a filter
- **When** `ReadAsync(filter, limit: 3)` is called
- **Then** Flux returns the first 3 entities of all 10 and the filter then removes the non-matching ones, so fewer than 3
  may come back (`AsyncInfluxDBStore.cs:404-447`)

#### Scenario: Updating an InfluxDB entity

- **Given** an entity written at time t1
- **When** it is updated at t2
- **Then** a second point is written at t2 and reads return the t2 values (`AsyncInfluxDBStore.cs:647`)

### Requirement: Deleting a missing item is silent everywhere except Cosmos DB

The system SHALL make single and bulk `Delete` of an entity that is not stored a no-op on SQL, MongoDB, RavenDB,
InfluxDB, JSON and XML. On Cosmos DB, `DeleteItemAsync` on a missing id SHALL propagate the driver's `CosmosException`
(404) untranslated, single or bulk, outside a batch. Every backend SHALL skip an entity whose Guid is `null` or empty.

#### Scenario: Cosmos DB delete of a missing item

- **Given** an `AsyncCosmosDBStore<T>` with no item for Guid G
- **When** `DeleteAsync(entity with G)` is called outside a batch
- **Then** a `CosmosException` with status 404 is thrown (`AsyncCosmosDBStore.cs:333`)

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

The system SHALL translate each assignment through `Match(set, increment)`:
- MongoDB (`MongoPropertyUpdateTranslator`): a set is `$set` with the value serialized through the member's BSON
  serializer (null as `BsonNull`); an increment is `$inc`, refused with `NotSupportedException` when the member is
  stored with a string representation.
- Elasticsearch (`ElasticSearchStoreHelper.BuildUpdateScript`): a Painless script `ctx._source.{storedField} = params.p_X`
  or `+=` for an increment, with a null value written as a literal `null`.

An assignment whose property cannot be resolved SHALL throw `ArgumentException` on both.

#### Scenario: Incrementing a string-stored decimal on MongoDB

- **Given** a decimal member mapped with `BsonRepresentation(BsonType.String)`
- **When** `UpdateAsync(filter, new PropertyUpdate<T>().Increment(x => x.Amount, 1))` runs
- **Then** `NotSupportedException` is thrown before any write (`MongoPropertyUpdateTranslator.cs:68-79`)

### Requirement: Async MongoDB, Cosmos DB and RavenDB save natively; their sync twins use the shared routing

The system SHALL implement `SaveAsync` on the async MongoDB, Cosmos DB and RavenDB stores as a native upsert
(`ReplaceOne` with `IsUpsert`, `UpsertItemAsync`, `StoreAsync` without a change vector), overriding the public wrapper.
Cosmos DB and RavenDB SHALL apply the store delegate **before** `EnsureGuid`, so a delegate on a new entity sees a null
Guid, where their create paths assign the Guid first. Because the update branch never passes through `UpdateAsync`, a
unique-index collision on these upserts SHALL surface as the driver's exception, not `StoreUniqueViolationException`.
The sync MongoDB, Cosmos DB and RavenDB stores, and every other backend, SHALL use the base `StoreSave` routing.
InfluxDB's `SaveAsync` routes to `CreateAsync` / `UpdateAsync`.

#### Scenario: Cosmos DB save of a new entity with a delegate

- **Given** an entity with `Guid == null` and a delegate that reads `Guid`
- **When** `AsyncCosmosDBStore.SaveAsync(entity, delegate)` runs
- **Then** the delegate sees `null` (`AsyncCosmosDBStore.cs:436-438`)

#### Scenario: MongoDB save colliding with a unique index

- **Given** a unique index on `Email` and a stored entity with an existing Guid
- **When** `SaveAsync` would give it an `Email` another document holds
- **Then** a `MongoWriteException` propagates untranslated

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
- single-file JSON: delete the file, then write it (not atomic).
- single-file XML: write `{path}.tmp`, then move it over the file (atomic replace).
- JSON separate-file: rewrite every entity's file on every write and delete matching files not in memory.
- XML separate-file: write only the touched entity's file.
- JSON and XML batch stores: require `BatchSettings`, refusing plain `Settings` with `InvalidDataException`.
- `JsonSeparateBulkStore` keeps its files directly in `Location` (not a `Name` subdirectory), and its `Destroy` deletes
  the `Location` directory itself, which fails if other files remain there.

The async JSON stores SHALL reload from disk whenever the in-memory set is empty; the async XML stores load once
(`_loaded`).

#### Scenario: Mutating an entity after creating it in a JSON store

- **Given** an entity created in a `JsonStore<T>`
- **When** the caller changes a property without calling `Update`
- **Then** `Read(guid)` returns the changed instance, though the file still holds the old value (`AbstractJsonStore.cs:95`)

#### Scenario: Crash during a JSON save

- **Given** a `JsonStore<T>` writing its file
- **When** the process stops after `File.Delete(Path)` and before the write completes
- **Then** the data file is gone or partial (`JsonStore.cs:249`), where `XmlStore` would keep the previous file

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
