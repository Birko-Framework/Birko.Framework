---
area: backend-store-implementations
generated-at: 6317c278f867a48eadd3f1690be4603d1ab0291d
generated-on: 2026-11-28
sources:
  - ../Birko.Data.CosmosDB/Repositories/AsyncCosmosDBModelRepository.cs
  - ../Birko.Data.CosmosDB/Repositories/CosmosDBModelRepository.cs
  - ../Birko.Data.CosmosDB/Stores/AsyncCosmosDBStore.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosChunkedWriteException.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosDBStore.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosDBStoreFactory.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosDateTimeOrder.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosFilterRewrite.DateTime.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosFilterRewrite.TimeSpan.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosFilterRewrite.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosInListChunking.cs
  - ../Birko.Data.CosmosDB/Stores/CosmosTransactionScopeException.cs
  - ../Birko.Data.CosmosDB/Stores/Settings.cs
  - ../Birko.Data.ElasticSearch/Aggregation/StoreAggregatePlan.cs
  - ../Birko.Data.ElasticSearch/Repositories/AsyncElasticSearchModelRepository.cs
  - ../Birko.Data.ElasticSearch/Repositories/AsyncElasticSearchRepository.cs
  - ../Birko.Data.ElasticSearch/Repositories/ElasticSearchModelRepository.cs
  - ../Birko.Data.ElasticSearch/Repositories/ElasticSearchRepository.cs
  - ../Birko.Data.ElasticSearch/Stores/AsyncElasticSearchStore.cs
  - ../Birko.Data.ElasticSearch/Stores/ElasticSearchStore.cs
  - ../Birko.Data.ElasticSearch/Stores/ElasticSearchStoreFactory.cs
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
  - ../Birko.Data.JSON/Stores/JsonStoreFactory.cs
  - ../Birko.Data.JSON/Stores/StoredKeyGuard.cs
  - ../Birko.Data.MongoDB/Repositories/AsyncMongoDBModelRepository.cs
  - ../Birko.Data.MongoDB/Repositories/AsyncMongoDBRepository.cs
  - ../Birko.Data.MongoDB/Repositories/MongoDBModelRepository.cs
  - ../Birko.Data.MongoDB/Repositories/MongoDBRepository.cs
  - ../Birko.Data.MongoDB/Stores/AsyncMongoDBStore.cs
  - ../Birko.Data.MongoDB/Stores/IMongoConnectedStore.cs
  - ../Birko.Data.MongoDB/Stores/MongoDBStore.cs
  - ../Birko.Data.MongoDB/Stores/MongoDBStoreFactory.cs
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
  - ../Birko.Data.RavenDB/Stores/RavenDBStoreFactory.cs
  - ../Birko.Data.RavenDB/Stores/RavenDatabaseBootstrap.cs
  - ../Birko.Data.RavenDB/Stores/RavenDocumentId.cs
  - ../Birko.Data.RavenDB/Stores/RavenIndexWait.cs
  - ../Birko.Data.RavenDB/Stores/RavenPropertyUpdatePatch.cs
  - ../Birko.Data.RavenDB/Stores/RavenQueryRetry.cs
  - ../Birko.Data.RavenDB/Stores/RavenTrackedCopy.cs
  - ../Birko.Data.RavenDB/Stores/Settings.cs
  - ../Birko.Data.SQL.MSSql/Repositories/AsyncMSSqlModelRepository.cs
  - ../Birko.Data.SQL.MSSql/Repositories/AsyncMSSqlRepository.cs
  - ../Birko.Data.SQL.MSSql/Repositories/MSSqlModelRepository.cs
  - ../Birko.Data.SQL.MSSql/Repositories/MSSqlRepository.cs
  - ../Birko.Data.SQL.MSSql/Stores/AsyncMSSqlStore.cs
  - ../Birko.Data.SQL.MSSql/Stores/MSSqlSettings.cs
  - ../Birko.Data.SQL.MSSql/Stores/MSSqlSqlStoreFactory.cs
  - ../Birko.Data.SQL.MSSql/Stores/MSSqlStore.cs
  - ../Birko.Data.SQL.MySQL/Repositories/AsyncMySQLModelRepository.cs
  - ../Birko.Data.SQL.MySQL/Repositories/AsyncMySQLRepository.cs
  - ../Birko.Data.SQL.MySQL/Repositories/MySQLModelRepository.cs
  - ../Birko.Data.SQL.MySQL/Repositories/MySQLRepository.cs
  - ../Birko.Data.SQL.MySQL/Stores/AsyncMySQLStore.cs
  - ../Birko.Data.SQL.MySQL/Stores/MySQLSqlStoreFactory.cs
  - ../Birko.Data.SQL.MySQL/Stores/MySQLStore.cs
  - ../Birko.Data.SQL.MySQL/Stores/MySqlSettings.cs
  - ../Birko.Data.SQL.PostgreSQL/Repositories/AsyncPostgreSQLModelRepository.cs
  - ../Birko.Data.SQL.PostgreSQL/Repositories/AsyncPostgreSQLRepository.cs
  - ../Birko.Data.SQL.PostgreSQL/Repositories/PostgreSQLModelRepository.cs
  - ../Birko.Data.SQL.PostgreSQL/Repositories/PostgreSQLRepository.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/AsyncPostgreSQLStore.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/PostgreSQLSqlStoreFactory.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/PostgreSQLStore.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/PostgreSqlSettings.cs
  - ../Birko.Data.SQL.SqLite/Repositories/AsyncSqLiteModelRepository.cs
  - ../Birko.Data.SQL.SqLite/Repositories/AsyncSqLiteRepository.cs
  - ../Birko.Data.SQL.SqLite/Repositories/SqLiteModelRepository.cs
  - ../Birko.Data.SQL.SqLite/Repositories/SqLiteRepository.cs
  - ../Birko.Data.SQL.SqLite/Stores/AsyncSQLiteStore.cs
  - ../Birko.Data.SQL.SqLite/Stores/SQLiteStore.cs
  - ../Birko.Data.SQL.SqLite/Stores/SqLiteSettings.cs
  - ../Birko.Data.SQL.SqLite/Stores/SqLiteSqlStoreFactory.cs
  - ../Birko.Data.SQL/Stores/IAsyncSqlBulkStore.cs
  - ../Birko.Data.SQL/Stores/ISqlStoreFactory.cs
  - ../Birko.Data.SQL/Stores/SqlStoreFactory.cs
  - ../Birko.Data.Stores/FileStoreFolders.cs
  - ../Birko.Data.Stores/IStoreFactory.cs
  - ../Birko.Data.Stores/StoreFactory.cs
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
  - ../Birko.Data.XML/Stores/XmlStoreFactory.cs
# source-commits: omitted — every source is inside this repo, so `generated-at` covers them (regen step 5c).
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-11-27 over 439 feature-linked tasks: 187 resolved, 252 did not -
# polyrepo-era `pr:` values that are not commits of this repo, or no reachable commit whose subject leads with the
# task id; 2 tasks the state gate refuses have commits leading with their id. Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 252
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
because settings were never applied — create, update, delete, save, read, count and aggregate — with
`InvalidOperationException` naming `SetSettings`. The refusal is in `InitCore(Async)`, which every public operation
reaches first (public overrides that answered without it — the sync JSON and XML `Read(Guid)`, MongoDB's `SaveAsync` and
`ReadAllAsync`, and `Aggregate` / `AggregateAsync` on InfluxDB, MongoDB and the async JSON and XML stores, which answered
an empty list until TASK-1003 — now open the gate). Decided 2026-11-04 (the user) and measured by `tests/Shared/StoreContract/UnconfiguredContract.cs` on every
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

#### Scenario: Aggregate on an unconfigured store (TASK-1003)

- **Given** an `InfluxDBStore<T>`, a MongoDB store or an async JSON or XML store whose `SetSettings` was never called
- **When** `Aggregate` / `AggregateAsync` runs with a `Count` aggregate
- **Then** `InvalidOperationException` names `SetSettings` (it answered an empty list), as `UnconfiguredContract` checks
  on every backend that runs it

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

### Requirement: InfluxDB's request timeout is a setting

The system SHALL build every InfluxDB client from `Settings.GetClientOptions()`, which applies `Settings.RequestTimeout` to
each HTTP request. `null` (the default) SHALL leave the connection string's `timeout` (milliseconds) or the client's own
10 s default; a value that is not positive or exceeds 4,294,967,294 ms SHALL be refused by the setter with
`ArgumentOutOfRangeException`, `Timeout.InfiniteTimeSpan` meaning no timeout; and a `RequestTimeout` set while the
connection string also carries a `timeout` SHALL throw `InvalidOperationException` when the client is built, rather than
pick one (TASK-908: a read over ~100k points took 45-50 s and failed at the 10 s default, measured).

#### Scenario: Two timeouts given (TASK-908)

- **Given** InfluxDB `Settings` with `RequestTimeout = 60 s` and a connection string `http://host:8086?timeout=30000`
- **When** a store's client is built
- **Then** `InvalidOperationException` names both timeouts

#### Scenario: A zero timeout (TASK-908)

- **Given** InfluxDB `Settings`
- **When** `RequestTimeout` is set to `TimeSpan.Zero`
- **Then** `ArgumentOutOfRangeException` is thrown

### Requirement: Elasticsearch and RavenDB stores starting together on a new index or database all start

The system SHALL make Elasticsearch initialization treat an index that exists, or a create refused with
`resource_already_exists_exception` (another client created it in between), as found, and in every case, a create of
its own included, wait until the index answers: a cluster health wait for yellow with a 30 s timeout, then a count
retried every 50 ms while its only failure is `no_shard_available_action_exception`, until 30 s have passed; a health
timeout, any other count failure, or the deadline throws `InvalidOperationException`, as any other create failure does
(TASK-997). RavenDB initialization (`RavenDatabaseBootstrap.EnsureReady`, sync, and `EnsureReadyAsync`, which the async
store's `InitCoreAsync` awaits) SHALL, while the database is not listed, send a create and retry it only when refused with
`DatabaseDisabledException` or `ConcurrencyException`, the two refusals a racing create gets (measured), after a pause of
50 ms, doubled while under 1 s, plus up to 50 ms of jitter; then send a statistics request, retried the same way while it
throws `DatabaseDisabledException`; both within 30 s, after which the last failure is thrown (TASK-996: concurrent creates
were refused, one with eight clients at once refusing them all, and a listed database could still be loading, measured).
Any other create failure SHALL be thrown at once (TASK-1004: every exception was retried, so a refused name took 31 s to
fail). The async walk SHALL send asynchronously and wait with `Task.Delay`, holding no thread (TASK-1005: it ran the sync
walk, and a first call on a disabled database returned after 31 s); init still runs under `CancellationToken.None`
(TASK-324), so the caller's token does not stop it.

#### Scenario: Two Elasticsearch stores initializing on a new index (TASK-997)

- **Given** two app instances whose stores initialize at the same moment on an index that does not exist
- **When** the second one's create is refused with `resource_already_exists_exception`
- **Then** it waits until the index answers a count, and its first query does not fail with "all shards failed"

#### Scenario: RavenDB stores starting together on a new database (TASK-996)

- **Given** several RavenDB stores initializing at once on a database that does not exist
- **When** the server refuses their creates as "already exists" or "unloaded and locked"
- **Then** each retries until the database is listed and answers a statistics request, and every store starts

#### Scenario: A RavenDB database name the server refuses (TASK-1004)

- **Given** a RavenDB store whose database name the server will never accept (`birko task1004 not permitted`)
- **When** the store initializes, sync or async
- **Then** the create's failure is thrown in under 5 s, without retrying (it took 31 s)

#### Scenario: An async RavenDB store's first call on a disabled database (TASK-1005)

- **Given** an `AsyncRavenDBStore<T>` on a database that exists and is disabled
- **When** `CountAsync()` is called
- **Then** it returns a pending `Task` within 2 s, while init waits for the database without holding the caller's thread

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
(index-backed, no wait for non-stale results), while `ReadRealtime(Async)` is the load by id. Every save a RavenDB store
makes on a session it opened itself, sync and async, SHALL go through `RavenIndexWait.SaveChanges(Async)`: the save is
sent on its own, with no server-side wait, and then, as a separate step, the store reads the index statistics and, for
each stale index covering its collection (`Conventions.GetCollectionName(typeof(T))`), runs an empty query that waits for
non-stale results, all within `RavenIndexWait.Timeout` (15 s): a write returns only once the indexes include it, so a
filtered read or count after it sees it, as on the other providers. The wait SHALL never throw and is not cancellable: a
timeout or a failed wait returns (the write is applied; a query may still briefly miss it), so an applied write is never
reported as failed (TASK-999, the user's decision: the wait used to be the server's own, inside the save, and under the
full suite's load the server cancelled it after committing, so the client reported the write as failed, measured in 2 of
4 full runs). `RavenIndexWait.Apply` returns the session unchanged. A session the caller supplies
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
- **Then** all 6 come back, because the update waited for the indexes after its save, before returning (a query could return 5)

#### Scenario: A RavenDB write whose index wait fails (TASK-999)

- **Given** an `AsyncRavenDBStore<T>` (or `RavenDBStore<T>`) with no `TransactionContext`
- **When** a write's save is applied and the index wait that follows times out or fails
- **Then** the write returns without throwing

### Requirement: RavenDB sends a request again when the server refused it having applied nothing

The system SHALL run every query of the RavenDB stores (filtered reads, first, count, the aggregate's read) and every
`SaveChanges` of a session they opened through `RavenQueryRetry`, which sends the request again on two refusals that
apply nothing (TASK-998): `IndexCreationException` (an auto index not created in time on a new database), at once, up to
3 attempts in all; and `DatabaseDisabledException` (a database briefly unloaded and locked by another client's create),
after a pause of 50 ms doubled while under 1 s, until 30 s from the first attempt. Any other exception, and the last
attempt's, SHALL be thrown. A load by id is not retried.

#### Scenario: A query whose auto index times out (TASK-998)

- **Given** a RavenDB store on a new database under concurrent load
- **When** a filtered read throws `IndexCreationException` once
- **Then** the query is sent again and its answer returned

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

### Requirement: Filtered Update and Delete run natively on SQL, MongoDB and Elasticsearch; RavenDB patches exact updates

The system SHALL run `Delete(filter)` and `Update(filter, PropertyUpdate<T>)` as:
- MongoDB: public overrides that rewrite `MemoryExtensions.Contains`, run `RequireFilter` and `RequireBoundedFilter`, then
  issue `DeleteMany` / `UpdateMany` with the translated update — bypassing `DeleteCore`, so a subclass's `DeleteCore` is
  not reached. No miss is reported. An empty `PropertyUpdate` returns without writing.
- Elasticsearch: public overrides that run `RequireBoundedFilter`, open the gate, and issue `_delete_by_query` /
  `_update_by_query`; any version conflict throws `StoreConcurrencyException` (with applied and conflict counts), any
  failure or invalid response `StoreException`, a timeout `StoreException`. The query matches the index as of its last
  refresh.
- SQL: native statements inherited from `DataBaseBulkStore` / `AsyncDataBaseBulkStore`.
- RavenDB `Update(filter, PropertyUpdate<T>)` and `UpdateAll(PropertyUpdate<T>)`, sync and async: public overrides that
  run `RequireFilter` and `RequireBoundedFilter` (the filtered form only) and return on an empty `PropertyUpdate`. When
  the store runs in no session (neither its own `TransactionContext` nor an ambient unit-of-work session) and
  `RavenPropertyUpdatePatch.TryBuild` yields a patch, they read the matching documents, defer one `PatchCommandData` per
  document id into one fresh session (through `RavenIndexWait.Apply`) and save it once: one transaction, the arithmetic
  run on the server, `*Core` not reached, no miss reported. A script failing its range check SHALL surface as
  `OverflowException` with nothing written to any document. Otherwise (a session, or no patch) the base implementation
  (TASK-827, the user's decision).
- RavenDB `Delete(filter)`, and Cosmos DB, InfluxDB, JSON and XML: the base implementation (read the matches, then write
  each through `*Core`); on Cosmos DB once per IN-list chunk when the filter is chunked (see the IN-list requirement).

`Update(filter, Action<T>)` SHALL be the base read-modify-save on every backend; on Cosmos DB, run once per IN-list chunk
when the filter is chunked.

#### Scenario: MongoDB filtered delete

- **Given** a MongoDB store subclass overriding `DeleteCore`
- **When** `DeleteAsync(x => x.Age > 30)` runs
- **Then** one `DeleteMany` is issued and the subclass's `DeleteCore` is not called (`AsyncMongoDBStore.cs:461-477`)

#### Scenario: Elasticsearch update-by-query hits a conflict

- **Given** a document changed between the query snapshot and the write
- **When** `UpdateAsync(filter, updates)` runs
- **Then** `StoreConcurrencyException` reports how many documents were applied before it stopped

#### Scenario: Parallel RavenDB increments outside a unit of work (TASK-827)

- **Given** an `AsyncRavenDBStore<T>` (or `RavenDBStore<T>`) with no session, holding one document with `Hits = 0`
- **When** 8 callers each run `UpdateAsync(x => x.Guid == g, new PropertyUpdate<T>().Increment(x => x.Hits, 1))` 10 times
  in parallel
- **Then** the document reads `Hits = 80` (the C# read-modify-save path lost increments, measured)
  (`tests/Birko.Data.RavenDB.Tests/Stores/Task827PatchTests.cs`)

#### Scenario: A RavenDB increment that overflows on one document (TASK-827)

- **Given** two matching documents, one with an `int` `Hits = 1` and one with `Hits = int.MaxValue`
- **When** `UpdateAsync(filter, new PropertyUpdate<T>().Increment(x => x.Hits, 1))` runs
- **Then** `OverflowException` is thrown and both documents keep their values

#### Scenario: A RavenDB update inside a unit of work (TASK-827)

- **Given** an `AsyncRavenDBStore<T>` whose `TransactionContext` is an active `RavenDbUnitOfWork`'s session
- **When** a `PropertyUpdate` increment runs and the unit of work rolls back
- **Then** the update took the base path in the unit of work's session, and the stored value is unchanged

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

On RavenDB, `RavenPropertyUpdatePatch.TryBuild` SHALL build one patch script only when every assignment is exact in a
double, since patch JavaScript holds every number as a double (measured on RavenDB 6.0): a set is
`this['{Property}'] = args.pN` for a (nullable) string, bool, Guid, `DateTime`, `DateTimeOffset`, `TimeSpan`, char, enum,
`byte` / `sbyte` / `short` / `ushort` / `int` / `uint`, or `long` whose value lies within ±(2^53 − 1); an increment of a
`short`, `int` or `long` property by a `short` / `int` delta, or a `long` delta within ±(2^53 − 1), adds to the stored
value (null read as 0) and throws `BIRKO_INCREMENT_OVERFLOW` unless the result is a safe integer within the type's range
(±(2^53 − 1) for `long`). Any other assignment (a `decimal`, `double` or `float` set or increment, an increment of a
nullable or other type, a nested member path, an unresolved property, any computed set) SHALL make `TryBuild` return
`null`, so the whole update stays on the exact C# path (TASK-827). A `long` counter past 2^53 therefore cannot be
incremented outside a unit of work on RavenDB.

An assignment whose property cannot be resolved SHALL throw `ArgumentException` on MongoDB and Elasticsearch.

#### Scenario: Incrementing a string-stored decimal on MongoDB

- **Given** a decimal member mapped with `BsonRepresentation(BsonType.String)`
- **When** `UpdateAsync(filter, new PropertyUpdate<T>().Increment(x => x.Amount, 1))` runs
- **Then** `NotSupportedException` is thrown before any write (`MongoPropertyUpdateTranslator.cs:157-169`)

#### Scenario: A computed set on MongoDB (TASK-826)

- **Given** a MongoDB store whose `Net` and `Gross` are stored as Decimal128
- **When** `UpdateAsync(filter, new PropertyUpdate<T>().Compute(x => x.Net, x => Math.Round(x.Gross / 1.2m, 2, MidpointRounding.ToEven)))` runs
- **Then** one pipeline update sets `Net` to `$round` of `$divide` of `$Gross` by 1.2, to 2 digits

#### Scenario: A decimal increment on RavenDB (TASK-827)

- **Given** a RavenDB document with `decimal Amount = 0.1m`
- **When** `UpdateAsync(filter, new PropertyUpdate<T>().Increment(x => x.Amount, 0.2m))` runs, then a set adds
  `98765432109876543.21m`
- **Then** `TryBuild` returns `null` both times, the C# path applies them, and `Amount` reads `98765432109876543.51m`

#### Scenario: A long past 2^53 on RavenDB (TASK-827)

- **Given** a RavenDB document with `long Total = 9007199254740993`
- **When** a `PropertyUpdate` increments `Total` by 1
- **Then** `OverflowException` is thrown and `Total` still reads `9007199254740993`

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

### Requirement: Cosmos DB splits a long IN list into chunks, or refuses it before any request

The Cosmos LINQ provider writes `list.Contains(x.Col)` into the query TEXT, and the server refuses a text over 524,288
characters (SC3020). The system SHALL, in `CosmosDBStore<T>` and `AsyncCosmosDBStore<T>`, plan every filtered read
(`Read`, `ReadFirst`), `Count`, `Update(filter, Action<T>)` (and so `Update(filter, PropertyUpdate<T>)`) and
`Delete(filter)` through `CosmosInListChunking.Plan` (TASK-834, TASK-841, TASK-842):
- An IN is a `Contains` whose list does not reference the document and whose item does; its values are deduplicated
  (default equality: ordinal for strings, one `null` kept). An empty list is never chunked; the caller's expression and
  list are never modified.
- A list is split when it holds more than `InListChunkSize` (1000) distinct values, or when the query text would exceed
  the budget (`MaxQueryTextLength` 524,288 less a sixteenth, 491,520). Within both, the filter runs as one query.
- Only the list with the largest estimated length is split. Chunks are filled while their count stays within 1000 and
  their estimated text (each value's JSON plus 2, plus the rest of the query) within the budget; each chunk is then
  measured with the store's own rendered query text and halved until it fits. A single-value chunk may use the full
  limit. A split that yields one chunk runs as the original query.
- A list may be split only when its element type is a value type or a string and the IN is a plain AND conjunct, and
  for reads only with no sort, limit or offset. A list that may not be split SHALL still run as one query while its text
  is within 524,288 characters; past it, `NotSupportedException` is thrown before any request, naming the operation,
  the value count, the length, the limit and the reason (under OR, NOT IN, under NOT, nested, non-scalar type, or sort /
  limit / offset), never the values.
- The `absent OR IN` the null semantics make of a list holding `null`, or of a list tested against a non-nullable member
  (TASK-899), splits as a plain AND-conjunct IN does: its absent leg goes with the leading chunk only (the chunk holding
  the member type's default is moved first), so the chunks stay disjoint.
- An aggregate's filter is planned as a sorted read is: never split, one query within 524,288 characters, and past it
  refused with the reason that groups and aggregates cannot be combined across chunks (TASK-876).
- A single value whose query alone exceeds the limit SHALL throw `NotSupportedException` naming its approximate
  length, not the value; a chunk still over the limit because the rest of the filter is too long throws the general
  refusal.
- Chunked reads concatenate the chunks' results in list order; `ReadFirst` returns the first chunk's match; counts are
  summed. Chunked writes run the base filtered write once per chunk, in order, with no rollback: a failure in the first
  chunk, and any `OperationCanceledException`, propagate as themselves; a later failure throws
  `CosmosChunkedWriteException` (a `StoreException`; `Operation`, `ChunksApplied`, `ChunkCount`, the cause as inner
  exception), whose message says the applied chunks stay and the failing chunk may be partly applied.
- `InListChunkSize` and `MaxQueryTextLength` are `protected virtual`; a chunk size below 1 throws
  `InvalidOperationException`.

#### Scenario: A filtered delete past the query-text ceiling (TASK-834)

- **Given** an `AsyncCosmosDBStore<T>` and a list of 20,000 Guids (13,105 is the measured one-query ceiling)
- **When** `DeleteAsync(x => ids.Contains(x.Guid))` runs
- **Then** the delete runs in chunks of 1000 and removes exactly the matched items
  (`tests/Birko.Data.CosmosDB.Tests/Task834InListChunkingTests.cs`)

#### Scenario: A sorted read past the ceiling (TASK-834)

- **Given** a list whose query text exceeds 524,288 characters
- **When** `ReadAsync(x => ids.Contains(x.Guid), orderBy)` runs
- **Then** `NotSupportedException` is thrown before any request, giving the sort, limit or offset as the reason

#### Scenario: A chunked update failing in its second chunk (TASK-834)

- **Given** a filtered update split into chunks, and a failure while writing the second chunk
- **When** `UpdateAsync(filter, action)` runs
- **Then** `CosmosChunkedWriteException` reports `ChunksApplied = 1`, and the first chunk's updates stay stored

#### Scenario: A list of long strings (TASK-841, TASK-842)

- **Given** 1,200 distinct 600-character strings in `names.Contains(x.Name)`
- **When** `ReadAsync` and `CountAsync` run
- **Then** the list is cut into chunks whose measured query text is within the budget, and the results equal one
  query's (`tests/Birko.Data.CosmosDB.Tests/Task842LongStringChunkingTests.cs`)

#### Scenario: One value too long for any chunk (TASK-842)

- **Given** a list holding one 600,000-character string
- **When** a filtered read runs
- **Then** `NotSupportedException` is thrown before any request, naming the lengths and not the value

#### Scenario: A long list holding null (TASK-899)

- **Given** 20,000 values and `null` in `codes.Contains(x.Code)` on a nullable `Code`
- **When** `ReadAsync` runs
- **Then** the list is split, only the first chunk also matches a null or missing `Code`, and each document comes back once

### Requirement: Elasticsearch splits a long terms list at its index's declared limit

The system SHALL pass each Elasticsearch store's terms limit to the filter translator per call: its index's
`IndexSettings.MaxTermsCount` (matched by the type's full name) when declared, otherwise the process-wide
`ElasticSearch.MaxTermsCount` default (TASK-851). The limit is declared, not read from the index; nothing shared is
written.

#### Scenario: An index with a lower terms limit (TASK-851)

- **Given** an Elasticsearch store whose `IndexSettings` for its type declare `MaxTermsCount = 1000`
- **When** `ReadAsync(x => ids.Contains(x.Guid))` runs with 2,500 ids
- **Then** the query is a should of three terms queries of at most 1000 terms, while another index keeps the default

### Requirement: An ordering against null matches nothing on MongoDB, Cosmos DB and RavenDB

The system SHALL fold a lifted ordering against null (`x.Score > nul` with `nul` null) to the `false` C# defines it as
(`NullOrderingFold`) before the filter reaches the driver: on MongoDB in every filtered read, count, `ReadFirst`, and in
the filtered `Delete` / `Update(filter, PropertyUpdate<T>)` after their scope guards; on Cosmos DB in its filter
pre-pass; on RavenDB in `RavenFilterRewriter`. A RavenDB filter that folds to `false` entirely SHALL be answered without a
query (no entity, an empty list, `0`, or the aggregate of no rows), since RavenDB refuses a constant predicate (TASK-867:
RavenDB matched every non-null `Score`, Cosmos DB missed rows under `!`, measured).

#### Scenario: A RavenDB read with an ordering against null (TASK-867)

- **Given** an `AsyncRavenDBStore<T>` whose documents hold `Score` values and nulls, and `int? nul = null`
- **When** `ReadAsync(x => x.Score > nul)` runs
- **Then** an empty list is returned without a query

### Requirement: Cosmos DB filters answer null and missing members as C# does

The system SHALL prepare every Cosmos DB filter (reads, counts, the reads behind filtered writes, an aggregate's WHERE)
with `CosmosFilterRewrite.Prepare`, refusing a shape no rendering answers as C# with `NotSupportedException` before any
request. Its null semantics (TASK-896, TASK-899) SHALL:
- render `== null` as null-or-missing (`NOT IS_DEFINED(x) OR IS_NULL(x)`) and `!= null` / `HasValue` as has-a-value;
- guard a comparison of a nullable operand with has-a-value wherever an undefined result would change the answer (under
  an odd number of `!`, inside a conditional's test or a comparison of booleans); `!=` is the guarded equality negated,
  and two nullable operands are also equal when both are null or missing;
- read `??` and `GetValueOrDefault` as has-a-value ? value : fallback, and a list holding `null` as null-or-missing OR
  IN the other values;
- read a non-nullable member the document omits as its type's default: against a value, `NOT IS_DEFINED(m) OR test`
  where the default passes the test; in a computed operand or against another member, `IS_DEFINED(m) ? m : default`;
- rewrite nested predicates (`Any`, `Where`, `Count`, `First`, …) the same way, positive inside; `All(p)` as
  `!Any(!p)`; a nested count as the length of an array, never a COUNT subquery, which the gateway query plan (every
  client but Windows x64) rejected (TASK-919);
- aggregate only the non-null values in a nested `Sum` / `Average` / `Min` / `Max` (TASK-920); on the gateway query plan
  such a subquery still cannot be planned, and the read throws.

#### Scenario: A negated comparison over a nullable member (TASK-896)

- **Given** documents with `Score` 10, 30, `null`, and one that omits `Score`
- **When** `ReadAsync(x => !(x.Score > 20))` runs on a Cosmos DB store
- **Then** the documents with 10, `null` and no `Score` come back, as C# answers

#### Scenario: A non-nullable member the document omits (TASK-899)

- **Given** a document written without its `int A`
- **When** `ReadAsync(x => x.A == 0)` runs
- **Then** that document comes back

#### Scenario: A nested Sum over nullable values (TASK-920)

- **Given** a document whose `Items` hold scores 10 and `null`, and `A = 5`
- **When** `ReadAsync(x => x.Items.Sum(i => i.Score) > x.A)` runs (local query plan)
- **Then** the document comes back (Cosmos DB's SUM of [10, null] was undefined)

### Requirement: Cosmos DB string comparisons are ordinal or refused

The system SHALL, in a Cosmos DB filter, translate `string.IsNullOrEmpty`, and translate an ordinal comparison
(`string.CompareOrdinal(a, b)` or `string.Compare(a, b, StringComparison.Ordinal)`) of a member against a value, compared
with 0, with null ordered first as in C#. It SHALL refuse with `NotSupportedException`, before any request,
`string.IsNullOrWhiteSpace` (Cosmos DB's `TRIM` keeps U+00A0, U+0085 and U+001C), a culture-sensitive or ignore-case
comparison (`string.Compare(a, b)`, `CompareTo`, …), a comparison of two members or of a computed operand, a comparison
result compared with anything but 0, and a compared value holding a character at or above U+D800, where Cosmos DB's
code-point order and .NET's UTF-16 order can disagree (TASK-896).

#### Scenario: A culture comparison (TASK-896)

- **Given** a Cosmos DB store
- **When** `ReadAsync(x => string.Compare(x.Code, "c2") > 0)` runs
- **Then** `NotSupportedException` is thrown before any request, pointing to `string.CompareOrdinal`

### Requirement: Cosmos DB compares a stored DateTime by its wall-clock key

The system SHALL compare a `DateTime` read from a Cosmos DB document by its wall-clock key, the stored text's first 19
characters and its fraction padded to seven digits, and a caller's value by the same key computed from its ticks, so its
kind does not matter, never by the stored text (TASK-902: the serializer writes as many fraction digits as needed and a
kind-dependent suffix, and 71 of 84 shapes answered other rows, measured). Against a value, `==`, `<`, `<=`, `>`, `>=`
SHALL be preceded by a text range on the member covering the value's second, which the index can serve. `Equals`,
`DateTime.Compare` / `CompareTo` compared with 0, a list's `Contains` (a list of keys) and a `.Date` compared with
another member SHALL be translated. The system SHALL refuse with `NotSupportedException`, before any request, a
comparison or list of a `DateTimeOffset` member, a member of a stored `DateTime` or `DateTimeOffset` (`Year`, `Ticks`, …)
other than those `.Date` forms, a `DateTime` computed from a member, and a `Compare` result compared with anything but 0.
A stored offset is assumed to be the reader's own.

#### Scenario: Fraction digits and kinds (TASK-902)

- **Given** documents storing `…T10:00:00Z` and `…T10:00:00.5Z`
- **When** `ReadAsync(x => x.At < new DateTime(…, 10, 0, 0, 500))` runs
- **Then** only the first comes back (by text, `"…00.5Z"` sorts before `"…00Z"`)

#### Scenario: A DateTimeOffset comparison (TASK-902)

- **Given** a Cosmos DB store with a `DateTimeOffset` member `O`
- **When** a filter compares `O` with a value
- **Then** `NotSupportedException` is thrown before any request

### Requirement: Cosmos DB orders a stored TimeSpan by its sign and magnitude

The system SHALL split an ordering (`<`, `<=`, `>`, `>=`) of a `TimeSpan` read from a Cosmos DB document by sign (the
stored text below `"0"` is negative) and compare a fixed-width 23-character magnitude key computed from the stored text,
a negative value by the reverse of its magnitude; a caller's value is keyed in C# (TASK-905: the constant format orders
neither the day count nor negative values as the value; 68 of 124 shapes answered other rows, measured). Equality against
a value and a list's `Contains` SHALL keep their rendering (the text is canonical); two members compared for equality
SHALL compare their texts. `Equals`, `TimeSpan.Compare` / `CompareTo` compared with 0 SHALL become comparisons. A member of a stored
`TimeSpan` (`Ticks`, `TotalSeconds`, …), arithmetic on one, any other `TimeSpan` method over one, and a `Compare` result
compared with anything but 0 SHALL be refused with `NotSupportedException` before any request.

#### Scenario: A day count against hours (TASK-905)

- **Given** documents storing `1.02:03:04` and `23:00:00`
- **When** `ReadAsync(x => x.Span > TimeSpan.FromHours(24))` runs
- **Then** only the first comes back (by text it sorted before the second)

### Requirement: A Cosmos DB read sorted by a DateTime orders within each second as C# does

The system SHALL, for a Cosmos DB read sorted by a `DateTime` member (not chunked), read with the caller's sort and then
reorder each run of rows that agree on the earlier sort keys and on that member's second: the `DateTime` by its ticks,
a later string key ordinally. On a paged read, a run at the page's start (with an offset) or at its full end SHALL be read
whole with a second query, and where one run covers the page the rows after it are read too, to place the page within
the run; if the documents changed between the reads, the page's own rows, sorted, are returned (TASK-902: within one
second the server ordered `"…00"`, `"…00+01:00"`, `"…00.0000001Z"`, `"…00.5Z"`, `"…00Z"`, measured). A sort by a
`DateTimeOffset` or a `TimeSpan` member SHALL be refused with `NotSupportedException` before any request (TASK-902,
TASK-905).

#### Scenario: Sorting by a DateTime within one second (TASK-902)

- **Given** documents at `10:00:00.5`, `10:00:00` and `10:00:01` (UTC)
- **When** `ReadAsync(null, OrderBy<T>.By(x => x.At))` runs
- **Then** they come back as `10:00:00`, `10:00:00.5`, `10:00:01` (the server sent `10:00:00.5` first)

#### Scenario: Sorting by a TimeSpan (TASK-905)

- **Given** a Cosmos DB store
- **When** a read is sorted by a `TimeSpan` member
- **Then** `NotSupportedException` is thrown before any request

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

### Requirement: Store aggregates answer as the in-memory aggregation, by backend

The system SHALL run a store's `Aggregate` / `AggregateAsync` as follows:
- Every store initializes first, as a read does (TASK-886: on a fresh Elasticsearch, RavenDB or Cosmos DB store the
  aggregate failed, or answered nothing on Cosmos DB; TASK-1003: InfluxDB, MongoDB and the async JSON and XML stores
  skipped the gate, so an unconfigured one answered an empty list).
- MongoDB and Cosmos DB refuse a string `Min` / `Max` asked in a `StringComparison` other than `Ordinal` with
  `NotSupportedException` before reading anything (TASK-907); MongoDB runs the pipeline with the simple collation, whatever
  the collection's default (TASK-909).
- Cosmos DB renders the query's filter as its reads do (the same pre-pass, then the read's `Where`; TASK-876: the filter
  was dropped and the whole container aggregated).
- RavenDB reads the filtered documents in one query, after `RavenFilterRewriter`, and aggregates them client-side with
  `AggregateHelper.LinqAggregate(Async)` for every GROUP BY shape (TASK-877: one GROUP BY field sent a facet query,
  which RavenDB refuses; TASK-868: the filter skipped the rewrite).
- InfluxDB always reads the filtered rows and aggregates them in memory (TASK-906: a native Flux query, used for one
  aggregate and no filter, answered otherwise in 24 of 27 cases, measured).
- Elasticsearch reads its composite aggregation page by page, 1000 groups per request.
Cosmos DB and Elasticsearch order and page the rows in memory, ordering strings by the query's `StringComparison`.

#### Scenario: A filtered Cosmos DB aggregate (TASK-876)

- **Given** a Cosmos DB store and an `AggregateQuery<T>` with a filter matching half the items
- **When** `AggregateAsync` runs
- **Then** only the matching items are aggregated

#### Scenario: A RavenDB aggregate grouped by one field (TASK-877)

- **Given** an `AsyncRavenDBStore<T>`
- **When** `AggregateAsync` runs with one GROUP BY field
- **Then** the documents are read with one query and grouped in memory, and no facet query is sent

### Requirement: File stores keep the caller's instances and differ in how they persist

The system SHALL, on the JSON and XML stores, keep entities in a non-concurrent `Dictionary<Guid, T>` holding the
caller's own instances (no copy on write or read), and persist as follows:
- every file a JSON or XML store writes is written through `Birko.Helpers.AtomicFile`: to `.{name}.{random}.tmp` beside
  it, then moved over it, so a failed write leaves the previous file and no temp file (TASK-721; JSON used to delete the
  file first, measured: an empty file after a failed write).
- JSON separate-file: rewrite every entity's file on every write and delete matching files not in memory.
- XML separate-file: write only the touched entity's file.
- XML batch: write every batch, then delete the batch files no longer written (they were deleted first, so a failed
  write lost them all: measured). Atomic per file, not across files. The batch files are loaded in batch-number order
  (`RecordFiles.ListByIndex`: `items-2` before `items-10`), so a save writes every entity back into the file it came from
  (TASK-931: they loaded in file-system order and were renumbered by it, so a save failing partway lost an entity,
  measured: 11 of 12).
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
again after `Destroy`; the async JSON stores re-read whenever they held no rows (measured: 4 reads for 3 calls). The
async XML separate and batch stores SHALL load in `InitCoreAsync`, before their first operation, a write included
(TASK-939: a fresh store whose first call was a write saw an empty store, measured: a batch create left 1 of 6 entities,
an update threw not-found and a delete did nothing).

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

#### Scenario: A fresh async XML batch store whose first call is a write (TASK-939)

- **Given** an `AsyncXmlBatchStore<T>` directory holding entities in several batch files, and a new store on it
- **When** its first call is a bulk create
- **Then** the stored entities are loaded first, and every batch file still holds its entities after the save

### Requirement: Backend repositories destroy their store twice

The system SHALL, in the MongoDB, InfluxDB, Cosmos DB, RavenDB, SQLite, PostgreSQL, MySQL and SQL Server repositories,
override `DestroyAsync` (and `Destroy` on the sync MongoDB and InfluxDB repositories) to call the base, which destroys
the store, and then destroy or drop the unwrapped store a second time. The TimescaleDB repository no longer does this.

#### Scenario: Destroying a RavenDB repository

- **Given** an `AsyncRavenDBModelRepository<T>`
- **When** `DestroyAsync()` is called
- **Then** the database delete runs once through the base and again on the unwrapped store
  (`AsyncRavenDBModelRepository.cs:69-72`)

### Requirement: Each SQL provider has one store factory, chosen by configuration

The system SHALL have one store factory per SQL provider, `SqLiteSqlStoreFactory`, `PostgreSQLSqlStoreFactory`,
`MySQLSqlStoreFactory` and `MSSqlSqlStoreFactory` (each a `SqlStoreFactory<TConnector>`), registered under `SqLite`,
`PostgreSQL`, `MySQL` and `MSSql` with `SqlStoreFactory.Create` and with `StoreFactory.Create` (`AddSqLite()`, …).
`GetAsyncStore<T>()` SHALL return a new async provider store on the factory's settings at every call; no factory hands
out a sync store. `FromConnectionString` SHALL accept only the keys the provider's settings carry, in any of the
driver's spellings, and SHALL refuse with `ArgumentException` any other key (named), a missing host, an SSL / encrypt
mode other than required or preferred (PostgreSQL `Require` / `Prefer`, MySQL `Required` / `Preferred`, SQL Server
`Encrypt=True` / `False`), a SQL Server named instance and `Persist Security Info=True`. SQLite SHALL take a file path, or
`Data Source` / `Filename` with an optional `Password`, made absolute, and its factory creates the folder when
constructed; an in-memory source (`:memory:`, or one starting `file::memory:`, in any case) SHALL be refused with
`ArgumentException` naming the InMemory backend and a file path, since the connector opens one connection per operation
and an in-memory database lasts one connection (TASK-1006: it became the path `<working directory>/:memory:`, and the
first operation failed with "unable to open database file", measured). The older per-provider factories (`SqLiteStoreFactory` and its peers, with their options and DI
extensions) no longer exist (TASK-1001).

#### Scenario: Getting stores from the SQLite factory (TASK-988)

- **Given** `SqlStoreFactory.Create("SqLite", "data/app.db", p => p.AddSqLite())`
- **When** `GetAsyncStore<T>()` is called twice
- **Then** each call returns a new `AsyncSQLiteStore<T>` on the absolute path of `data/app.db`, whose folder exists

#### Scenario: An in-memory SQLite source (TASK-1006)

- **Given** the SQLite connection string `Data Source=:memory:` (or `:memory:`, or `file::memory:?cache=shared`)
- **When** `SqlStoreFactory.Create("SqLite", …, p => p.AddSqLite())` runs
- **Then** `ArgumentException` names the InMemory backend and a file path, and no factory is returned

#### Scenario: A key the settings cannot carry (TASK-988)

- **Given** the PostgreSQL connection string `Host=h;Database=d;Pooling=false`
- **When** `SqlStoreFactory.Create("PostgreSQL", …, p => p.AddPostgreSQL())` runs
- **Then** `ArgumentException` names `Pooling`, and no factory is returned

### Requirement: Each non-SQL backend has a store factory holding one store per type

The system SHALL give MongoDB, Elasticsearch, RavenDB, Cosmos DB, JSON and XML a factory registered with
`StoreFactory.Create` under `MongoDB`, `ElasticSearch`, `RavenDB`, `CosmosDB`, `Json` and `Xml` (`AddMongoDB()`, …),
which parses its own connection string into its `Settings`, refusing any other key (compared ignoring case, spaces and
underscores) and a missing part with `ArgumentException`, and whose `GetAsyncStore<T>()` returns the same async store for
a type every time, since each store holds its data or builds its own client (TASK-990). The strings: MongoDB its own
`mongodb://` / `mongodb+srv://` URL, used verbatim, naming the database in its path; Elasticsearch
`Url=http(s)://…;IndexPrefix=…`; RavenDB `Url=http(s)://…;Database=…`; Cosmos DB `AccountEndpoint=…;AccountKey=…;Database=…`
and optionally `ConnectionMode=Direct|Gateway`, its factory refusing a `Settings.UserName` (one container for every type);
JSON and XML a folder path or `Path=…`, made absolute and created, each type an `AsyncJsonStore<T>` / `AsyncXmlStore<T>`
on `{type full name}.json` / `.xml` there.

#### Scenario: A MongoDB connection string without a database (TASK-990)

- **Given** `mongodb://host:27017`
- **When** `StoreFactory.Create("MongoDB", …, p => p.AddMongoDB())` runs
- **Then** `ArgumentException` asks for the database in the connection string's path

#### Scenario: One JSON store per type (TASK-990)

- **Given** a factory from `StoreFactory.Create("Json", "data/json", p => p.AddJson())`
- **When** `GetAsyncStore<Customer>()` is called twice
- **Then** the same `AsyncJsonStore<Customer>` is returned, on the file named by `Customer`'s full name plus `.json` in the absolute `data/json`
