---
area: migrations
generated-at: 5e214e7
generated-on: 2026-08-18
sources:
  - ../Birko.Data.Migrations.CosmosDB/Context/CosmosDBDataMigrator.cs
  - ../Birko.Data.Migrations.CosmosDB/Context/CosmosDBMigrationContext.cs
  - ../Birko.Data.Migrations.CosmosDB/Context/CosmosDBSchemaBuilder.cs
  - ../Birko.Data.Migrations.CosmosDB/CosmosMigrationRunner.cs
  - ../Birko.Data.Migrations.CosmosDB/CosmosMigrationStore.cs
  - ../Birko.Data.Migrations.CosmosDB/Settings/CosmosMigrationSettings.cs
  - ../Birko.Data.Migrations.ElasticSearch/Context/ElasticSearchDataMigrator.cs
  - ../Birko.Data.Migrations.ElasticSearch/Context/ElasticSearchMigrationContext.cs
  - ../Birko.Data.Migrations.ElasticSearch/Context/ElasticSearchSchemaBuilder.cs
  - ../Birko.Data.Migrations.ElasticSearch/ElasticSearchMigrationRunner.cs
  - ../Birko.Data.Migrations.ElasticSearch/ElasticSearchMigrationStore.cs
  - ../Birko.Data.Migrations.ElasticSearch/Settings/ElasticSearchMigrationSettings.cs
  - ../Birko.Data.Migrations.InfluxDB/Context/InfluxDBDataMigrator.cs
  - ../Birko.Data.Migrations.InfluxDB/Context/InfluxDBMigrationContext.cs
  - ../Birko.Data.Migrations.InfluxDB/Context/InfluxDBSchemaBuilder.cs
  - ../Birko.Data.Migrations.InfluxDB/InfluxMigrationRunner.cs
  - ../Birko.Data.Migrations.InfluxDB/InfluxMigrationStore.cs
  - ../Birko.Data.Migrations.MongoDB/Context/MongoDataMigrator.cs
  - ../Birko.Data.Migrations.MongoDB/Context/MongoMigrationContext.cs
  - ../Birko.Data.Migrations.MongoDB/Context/MongoSchemaBuilder.cs
  - ../Birko.Data.Migrations.MongoDB/MongoMigrationRunner.cs
  - ../Birko.Data.Migrations.MongoDB/MongoMigrationStore.cs
  - ../Birko.Data.Migrations.MongoDB/Settings/MongoMigrationSettings.cs
  - ../Birko.Data.Migrations.RavenDB/Context/RavenDBDataMigrator.cs
  - ../Birko.Data.Migrations.RavenDB/Context/RavenDBMigrationContext.cs
  - ../Birko.Data.Migrations.RavenDB/Context/RavenDBSchemaBuilder.cs
  - ../Birko.Data.Migrations.RavenDB/RavenMigrationRunner.cs
  - ../Birko.Data.Migrations.RavenDB/RavenMigrationStore.cs
  - ../Birko.Data.Migrations.RavenDB/Settings/RavenMigrationSettings.cs
  - ../Birko.Data.Migrations.SQL/Context/SchemaField.cs
  - ../Birko.Data.Migrations.SQL/Context/SqlDataMigrator.cs
  - ../Birko.Data.Migrations.SQL/Context/SqlMigrationContext.cs
  - ../Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs
  - ../Birko.Data.Migrations.SQL/CreateTablesMigration.cs
  - ../Birko.Data.Migrations.SQL/Settings/SqlMigrationSettings.cs
  - ../Birko.Data.Migrations.SQL/SqlMigrationRunner.cs
  - ../Birko.Data.Migrations.SQL/SqlMigrationStore.cs
  - ../Birko.Data.Migrations.SQL/SqlScriptMigration.cs
  - ../Birko.Data.Migrations.TimescaleDB/Context/TimescaleDBMigrationContext.cs
  - ../Birko.Data.Migrations.TimescaleDB/TimescaleDBMigration.cs
  - ../Birko.Data.Migrations.TimescaleDB/TimescaleDBMigrationRunner.cs
  - ../Birko.Data.Migrations/AbstractMigration.cs
  - ../Birko.Data.Migrations/AbstractMigrationRunner.cs
  - ../Birko.Data.Migrations/Context/IDataMigrator.cs
  - ../Birko.Data.Migrations/Context/IMigrationContext.cs
  - ../Birko.Data.Migrations/Exceptions/MigrationException.cs
  - ../Birko.Data.Migrations/IMigration.cs
  - ../Birko.Data.Migrations/IMigrationRunner.cs
  - ../Birko.Data.Migrations/IMigrationStore.cs
  - ../Birko.Data.Migrations/MigrationDirection.cs
  - ../Birko.Data.Migrations/MigrationResult.cs
source-commits:   # sibling HEADs when this spec was last written (2026-07-30 16:19:33,
                  # commit d40aba2). Reconstructed 2026-08-16 -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Data.Migrations: 4dd7e1b
  ../Birko.Data.Migrations.CosmosDB: 9755c1a
  ../Birko.Data.Migrations.ElasticSearch: e244e85
  ../Birko.Data.Migrations.InfluxDB: 23b63c3
  ../Birko.Data.Migrations.MongoDB: 8a7acf5
  ../Birko.Data.Migrations.RavenDB: 99d8d33
  ../Birko.Data.Migrations.SQL: 14896a0ef3b2b0c8bbd0b809846338bd3c59bd09
  ../Birko.Data.Migrations.TimescaleDB: 531d816
shaped-by: [FEATURE-014]
shaped-by-derived: true
shaped-by-unresolved: 80
---

# Schema/data migration runner and backend contexts

## Purpose

This capability lets an application evolve its persistent store in version-tracked steps. A
consumer writes migrations — each a numbered `Up`/`Down` pair — registers them with a per-backend
runner, and calls `Migrate()` or `Rollback(version)`. The runner asks a *migration store* which
versions have already been applied, decides which migrations fall in the requested range, executes
them in order, and records each one it applied.

Migrations are written against a platform-neutral `IMigrationContext`, which exposes a schema
builder (create/drop collections, add/drop/rename fields, create/drop indexes), a data migrator
(update/delete/count/copy/bulk-insert documents using a small Mongo-style JSON filter dialect), a
provider name, and a `Raw()` escape hatch handing back the native client. Seven backends implement
the contract — SQL (with MSSql/MySQL/PostgreSQL/SQLite dialects through a connector), TimescaleDB
(a specialisation of SQL), MongoDB, ElasticSearch, RavenDB, CosmosDB and InfluxDB — and they do
**not** all honour the neutral contract to the same degree. The divergences are recorded explicitly
below because they are what a consumer actually has to plan around.

## Requirements

### Requirement: Migration identity and metadata

The system SHALL identify each migration by a `long Version`, a `string Name`, a `string
Description` and a `DateTime CreatedAt`. `AbstractMigration` SHALL default `Description` to `Name`
and SHALL set `CreatedAt` to `DateTime.UtcNow` at construction time. `AbstractMigration.ToString()`
SHALL return `"Migration {Version}: {Name}"`.

#### Scenario: Description defaults to Name

- **Given** a class deriving from `AbstractMigration` that overrides only `Version` and `Name`
- **When** its `Description` property is read
- **Then** it returns the same string as `Name`

#### Scenario: CreatedAt is capture time, not authoring time

- **Given** a migration type deriving from `AbstractMigration`
- **When** two instances are constructed at different moments
- **Then** each instance's `CreatedAt` is the `DateTime.UtcNow` of its own construction, so the
  value recorded in the migration store reflects when the process created the object, not when the
  migration was written

#### Scenario: TimescaleDB migrations do not inherit the defaults

- **Given** `TimescaleDBMigration`, which implements `IMigration` directly rather than deriving from
  `AbstractMigration`
- **When** a subclass is written
- **Then** `Version`, `Name`, `Description`, `CreatedAt`, `Up` and `Down` are all `abstract` and must
  be supplied by the subclass — there is no `Description => Name` default and no `CreatedAt =
  DateTime.UtcNow`

### Requirement: Down is unimplemented unless overridden

The system SHALL have `AbstractMigration.Down(IMigrationContext)` throw
`NotImplementedException` with the message `"Down migration for '{Name}' (v{Version}) is not
implemented."` when a subclass does not override it.

#### Scenario: Rolling back a migration with no Down

- **Given** a registered `AbstractMigration` subclass that overrides `Up` only
- **When** `Rollback` selects it for execution
- **Then** `Down` throws `NotImplementedException`, which the runner wraps and rethrows as a
  `MigrationException`

#### Scenario: SqlScriptMigration with no DownSql

- **Given** a `SqlScriptMigration` subclass that supplies `UpSql` but leaves `DownSql` at its `null`
  default
- **When** `Down(context)` is called
- **Then** it delegates to `AbstractMigration.Down`, i.e. throws `NotImplementedException`

### Requirement: Migration registration rejects duplicate versions and keeps versions sorted

The system SHALL accept migrations via `RegisterMigrations(params IMigration[])` or
`RegisterMigrations(IEnumerable<IMigration>)`, SHALL ignore a null array/enumerable and null
elements, SHALL throw `InvalidOperationException` when a migration whose `Version` is already
registered is added, and SHALL keep the internal list sorted ascending by `Version` after every
registration call.

#### Scenario: Duplicate version rejected

- **Given** a runner with a migration of `Version = 3` already registered
- **When** `RegisterMigrations(anotherMigrationWithVersion3)` is called
- **Then** `InvalidOperationException` is thrown with message `"A migration with version 3 is
  already registered."`

#### Scenario: Nulls are skipped, not rejected

- **Given** a runner
- **When** `RegisterMigrations(null)` is called, and separately `RegisterMigrations(new IMigration[]
  { null, migrationV1 })`
- **Then** the first call returns without effect and the second registers only `migrationV1`

#### Scenario: Out-of-order registration is normalised

- **Given** migrations registered in the order 5, 1, 3
- **When** `Migrations` is read
- **Then** it yields them ordered 1, 3, 5

### Requirement: LatestVersion is the maximum registered version

The system SHALL report `LatestVersion` as `Migrations.Max(m => m.Version)`, or `0` when no
migration is registered.

#### Scenario: No migrations registered

- **Given** a freshly constructed runner
- **When** `LatestVersion` is read
- **Then** it returns `0`

### Requirement: CurrentVersion is the maximum applied version

The system SHALL report `CurrentVersion` by delegating to `IMigrationStore.GetCurrentVersion()`, and
every migration store SHALL compute it as `GetAppliedVersions().Max()`, or `0` when the applied set
is empty.

#### Scenario: Nothing applied yet

- **Given** a migration store whose applied-version set is empty
- **When** `GetCurrentVersion()` is called
- **Then** it returns `0`

#### Scenario: Gaps in the applied set do not lower the current version

- **Given** applied versions `{1, 2, 5}`
- **When** `GetCurrentVersion()` is called
- **Then** it returns `5` — the store reports the maximum, not the highest contiguous version

### Requirement: The runner must be initialized before migrating

The system SHALL throw `InvalidOperationException` with the message `"Migration runner has not been
initialized. Call Initialize() first."` from `Migrate`, `MigrateAsync`, `Rollback`, `RollbackAsync`,
`GetPendingMigrations` and `GetAppliedMigrations` when neither `Initialize()` nor `InitializeAsync()`
has completed. Both initialize methods SHALL be idempotent (returning immediately once the flag is
set), `Initialize()` SHALL call `IMigrationStore.Initialize()`, and `InitializeAsync()` SHALL call
`IMigrationStore.InitializeAsync(cancellationToken)`.

#### Scenario: Migrating without initializing

- **Given** a runner with migrations registered but `Initialize()` never called
- **When** `Migrate()` is called
- **Then** `InvalidOperationException` is thrown and no migration runs

#### Scenario: MigrateAsync does not self-initialize

- **Given** a runner on which only the synchronous `Initialize()` was never called and
  `InitializeAsync()` was never awaited
- **When** `MigrateAsync()` is awaited
- **Then** the synchronous initialization guard throws `InvalidOperationException` — the async entry
  point does not initialize on demand

#### Scenario: CurrentVersion bypasses the guard

- **Given** an uninitialized runner
- **When** `CurrentVersion` is read
- **Then** it queries the store directly and returns a value without the initialization check

### Requirement: Migrate resolves its target and refuses downgrades

The system SHALL treat a null `targetVersion` as `LatestVersion`. When the resolved target equals the
current version it SHALL return a successful `MigrationResult` with `FromVersion == ToVersion ==
current`, `Direction = Up` and an empty `ExecutedMigrations`. When the target is **less** than the
current version it SHALL return a failed `MigrationResult` whose `ErrorMessage` is `"Target version
{target} is less than current version {current}. Use Rollback for downgrades."` and whose
`ToVersion` equals `FromVersion`. Otherwise it SHALL call `ExecuteMigrations(current, target, Up)`.

#### Scenario: Already at latest

- **Given** applied versions `{1, 2}` and registered versions `{1, 2}`
- **When** `Migrate()` is called
- **Then** the result has `Success = true`, `FromVersion = 2`, `ToVersion = 2` and no executed
  migrations

#### Scenario: Migrate called with a lower target

- **Given** current version `5`
- **When** `Migrate(3)` is called
- **Then** the result has `Success = false`, `FromVersion = 5`, `ToVersion = 5`, `Direction = Up` and
  the "Use Rollback for downgrades" message; no migration is executed and no exception is thrown

#### Scenario: MigrateAsync reads the current version asynchronously

- **Given** an initialized runner
- **When** `MigrateAsync()` is awaited
- **Then** the current version comes from `await Store.GetCurrentVersionAsync(cancellationToken)`
  rather than the synchronous `CurrentVersion` property

### Requirement: Rollback refuses upgrades

The system SHALL return a successful, empty `MigrationResult` with `Direction = Down` when
`targetVersion` equals the current version, SHALL return a failed result with `ErrorMessage`
`"Target version {target} is greater than current version {current}. Use Migrate for upgrades."`
when `targetVersion` is greater, and otherwise SHALL call `ExecuteMigrations(current, targetVersion,
Down)`.

#### Scenario: Rollback to a higher version

- **Given** current version `2`
- **When** `Rollback(7)` is called
- **Then** the result has `Success = false`, `Direction = Down`, `FromVersion = ToVersion = 2` and
  the "Use Migrate for upgrades" message

#### Scenario: Rollback to zero

- **Given** current version `3` and registered migrations `{1, 2, 3}`
- **When** `Rollback(0)` is called
- **Then** migrations 3, 2, 1 are executed `Down` in that order

### Requirement: Migration selection is a version-range window, not an applied-set difference

The system SHALL select `Up` migrations as `Version > fromVersion && Version <= toVersion` ordered
ascending, and `Down` migrations as `Version <= fromVersion && Version > toVersion` ordered
descending. `GetPendingMigrations()` and `GetAppliedMigrations()` SHALL, by contrast, be computed
against `IMigrationStore.GetAppliedVersions()` — pending is every registered migration whose version
is not in the applied set, applied is every registered migration whose version is in it.

#### Scenario: Up range excludes the current version and includes the target

- **Given** registered versions `{1, 2, 3, 4}` and current version `2`
- **When** `Migrate(4)` is called
- **Then** exactly migrations 3 and 4 are executed, in that order

#### Scenario: Down range includes the current version and excludes the target

- **Given** registered versions `{1, 2, 3}` and current version `3`
- **When** `Rollback(1)` is called
- **Then** migrations 3 and 2 are executed `Down`, and migration 1 is left applied

#### Scenario: A back-dated unapplied migration is reported pending but never migrated

- **Given** applied versions `{1, 5}` and a newly registered migration of `Version = 3`
- **When** `GetPendingMigrations()` is called and then `Migrate()` is called
- **Then** `GetPendingMigrations()` includes migration 3, but `Migrate()` computes `current = 5`,
  `target = 5`, returns the already-at-target successful result, and migration 3 is never executed

### Requirement: The asynchronous execution hook falls back to blocking synchronous execution

The system SHALL provide `AbstractMigrationRunner.ExecuteMigrationsAsync` whose default
implementation calls `cancellationToken.ThrowIfCancellationRequested()` and then returns
`Task.FromResult(ExecuteMigrations(...))`. No shipped backend runner overrides this hook — the SQL,
TimescaleDB, MongoDB, ElasticSearch, RavenDB, CosmosDB and InfluxDB runners all override only the
synchronous `ExecuteMigrations`.

#### Scenario: MigrateAsync performs synchronous I/O

- **Given** any of the seven shipped runners, initialized
- **When** `MigrateAsync()` is awaited
- **Then** the migration bodies and store writes execute on the calling thread through the
  synchronous `ExecuteMigrations` path

#### Scenario: Cancellation is observed only before execution begins

- **Given** a cancellation token that is already cancelled
- **When** `ExecuteMigrationsAsync` is invoked
- **Then** `OperationCanceledException` is thrown before any migration runs; a token cancelled *after
  the call starts* has no effect on the running batch

### Requirement: An execution failure raises MigrationException rather than a failed result

The system SHALL, for every backend runner, throw `MigrationException` when a migration body or a
store record write throws, and SHALL NOT return a `MigrationResult` with `Success = false` for an
execution error. A non-successful `MigrationResult` is produced only by the target/direction guards
described above. `MigrationException` SHALL carry the offending `IMigration` in `Migration` and the
`MigrationDirection` in `Direction`, and the original exception as `InnerException`.

#### Scenario: A throwing migration surfaces as an exception

- **Given** a registered migration whose `Up` throws `InvalidOperationException`
- **When** `Migrate()` is called
- **Then** a `MigrationException` is thrown whose `InnerException` is the `InvalidOperationException`
  and whose `Migration` is that migration; the caller never receives a `MigrationResult`

#### Scenario: Per-backend failure messages

- **Given** the same failing migration on each backend
- **When** `Migrate()` is called
- **Then** the message is `"Migration failed. Changes have been rolled back."` (SQL, transactional),
  `"Migration failed. Database may be in an inconsistent state."` (SQL, non-transactional),
  `"Migration failed. Changes rolled back if session was used."` (MongoDB), `"Migration failed.
  ElasticSearch state may be inconsistent."`, `"Migration failed. RavenDB state may be
  inconsistent."`, `"Migration failed. Cosmos DB state may be inconsistent."`, or `"Migration
  failed. InfluxDB state may be inconsistent."`

#### Scenario: Blamed migration is derived from the executed count

- **Given** a batch of three migrations where the second one's `Up` throws
- **When** the runner builds the `MigrationException`
- **Then** it blames `migrations[executed.Count]` — index 1, the second migration — because one had
  already been appended to `executed`

#### Scenario: MongoDB distinguishes a post-loop commit failure

- **Given** a MongoDB batch where every migration succeeded but `session.CommitTransaction()` throws
- **When** the exception is built
- **Then** because `executed.Count == migrations.Count`, the runner uses the message-only constructor
  (`"Migration transaction failed to commit. Changes rolled back if session was used."`) with no
  `Migration` attached, instead of indexing past the end of the list

### Requirement: Empty selection short-circuits before any connection work

The system SHALL, in every backend `ExecuteMigrations`, return `MigrationResult.Successful(from, to,
direction, empty)` immediately when the computed migration list is empty — before opening a
connection, starting a transaction or constructing a context.

#### Scenario: Target inside a gap with no matching migrations

- **Given** registered versions `{1, 10}` and current version `1`
- **When** `Migrate(5)` is called
- **Then** no migration matches `1 < v <= 5`, and the result is successful with `FromVersion = 1`,
  `ToVersion = 5` and no executed migrations — even though the reported `ToVersion` does not
  correspond to any applied version

### Requirement: SQL migrations run in a single transaction by default, with version bookkeeping on the same connection

The system SHALL, when `SqlMigrationSettings.UseTransaction` is `true` (the default), open one
connection, begin one transaction, execute every selected migration and write each version row
inside that transaction, then commit. `SqlMigrationRunner` SHALL write the applied/removed version
row through the internal `SqlMigrationStore.RecordMigration(connection, transaction, migration)` /
`RemoveMigration(connection, transaction, migration)` overloads so that no second connection is
opened. On failure it SHALL attempt `transaction.Rollback()`, swallow any exception raised by the
rollback itself, and throw `MigrationException`. When `UseTransaction` is `false` it SHALL execute
each migration and version write on the shared open connection with no transaction.

#### Scenario: DDL and version row commit atomically

- **Given** `UseTransaction = true` and a migration that creates a table
- **When** `Migrate()` succeeds
- **Then** both the `CREATE TABLE` and the `INSERT` into the migrations table were issued on the same
  connection and transaction, and became visible together at commit

#### Scenario: Rollback failure does not mask the original error

- **Given** a failing migration whose subsequent `transaction.Rollback()` also throws
- **When** `Migrate()` is called
- **Then** the rollback exception is swallowed and the `MigrationException` still wraps the original
  migration failure

#### Scenario: Non-transactional mode leaves partial work applied

- **Given** `UseTransaction = false` and a two-migration batch where the second fails
- **When** `Migrate()` is called
- **Then** the first migration and its version row remain committed, and the thrown
  `MigrationException` message is `"Migration failed. Database may be in an inconsistent state."`

### Requirement: The SQL migration store provisions and reads its own tracking table

The system SHALL, on `Initialize()`/`InitializeAsync()`, probe for the migrations table and create it
when absent with columns `Version BIGINT PRIMARY KEY`, `Name VARCHAR(255) NOT NULL`, `Description
TEXT`, `CreatedAt TIMESTAMP NOT NULL` and `AppliedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP`.
The existence probe SHALL be `SELECT 1 FROM {FullTableName} WHERE 1 = 0` executed as a non-query,
SHALL treat a `DbException` as "table absent", and SHALL let any non-`DbException` propagate.
`GetAppliedVersions()` SHALL return an empty set when the probe reports the table absent.

#### Scenario: First run creates the tracking table

- **Given** a database with no `"__Migrations"` table
- **When** `Initialize()` is called
- **Then** the probe raises a `DbException`, which is caught, and the `CREATE TABLE` statement runs

#### Scenario: A closed connection is not mistaken for a missing table

- **Given** a connection that raises a non-`DbException` (e.g. `InvalidOperationException` for
  misuse)
- **When** the probe runs
- **Then** that exception propagates rather than being read as "no table"

#### Scenario: A transient lock is read as a missing table

- **Given** a probe that fails with a `DbException` because the database is momentarily locked
- **When** `Initialize()` runs
- **Then** the store concludes the table is absent and attempts `CREATE TABLE`

#### Scenario: Version rows are parameterised

- **Given** a migration whose `Name` contains a single quote
- **When** `RecordMigration` runs
- **Then** `Version`, `Name`, `Description` and `CreatedAt` are bound as `@Version`, `@Name`,
  `@Description`, `@CreatedAt` parameters, with `null` values converted to `DBNull.Value`

#### Scenario: Standalone store writes wrap themselves in a transaction

- **Given** the public `IMigrationStore.RecordMigration(migration)` (no connection argument)
- **When** it is called
- **Then** the store opens its own connection, begins a transaction, writes the row, commits — and
  on failure rolls back and rethrows

### Requirement: SQL migration table naming and quoting are configurable

The system SHALL default `SqlMigrationSettings.MigrationsTable` to `"__Migrations"`, `Schema` to
`null`, `UseTransaction` to `true` and `TransactionTimeout` to `30`. `FullTableName` SHALL be the
quoted table name alone when `Schema` is null or empty, and `"{quotedSchema}.{quotedTable}"`
otherwise. `QuoteIdentifier` SHALL wrap the identifier in ANSI double quotes and double any embedded
double quote. `SqlMigrationStore` SHALL take its own `quoteOpen`/`quoteClose` for the tracking
table's column identifiers, both defaulting to `"` — and no shipped code path supplies anything else:
`SqlMigrationRunner` constructs the store as `new SqlMigrationStore(factory, settings)` with both
quote arguments left at their defaults, and no provider-specific `SqlMigrationSettings` subclass
overriding the `protected virtual QuoteIdentifier` exists, so the tracking table is ANSI
double-quoted on every provider regardless of the connector's own quoting.

#### Scenario: Schema-qualified table name

- **Given** `Schema = "audit"` and `MigrationsTable = "__Migrations"`
- **When** `FullTableName` is read
- **Then** it returns `"audit"."__Migrations"`

#### Scenario: Identifier with an embedded quote

- **Given** `MigrationsTable = "we\"ird"`
- **When** `FullTableName` is read
- **Then** the embedded quote is doubled, producing `"we""ird"`

#### Scenario: Settings can be cloned off a RemoteSettings chain

- **Given** an existing `Birko.Configuration.RemoteSettings` instance
- **When** `new SqlMigrationStore(factory, remoteSettings)` is constructed
- **Then** the whole inherited settings chain is copied via `settings.LoadFrom(remoteSettings)`
  rather than by hand-listing properties

#### Scenario: The tracking table DDL does not vary by provider

- **Given** a `SqlMigrationRunner` built over a `MySQLConnector` (backtick quoting) or an
  `MSSqlConnector`
- **When** `Initialize()` provisions the tracking table
- **Then** the emitted DDL is the same ANSI-double-quoted `CREATE TABLE "__Migrations" ("Version"
  BIGINT PRIMARY KEY, … "AppliedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)` as on SQLite —
  neither the connector's `QuoteIdentifier` nor any provider type mapping is consulted, so the
  statement is only valid on providers that accept ANSI quotes and that `TIMESTAMP` spelling

### Requirement: SqlScriptMigration executes raw scripts on the runner's connection

The system SHALL run `UpSql` on apply and `DownSql` on revert against the `SqlMigrationContext`'s
`Connection`, attaching `Transaction` when one is active, in a single `ExecuteNonQuery`. It SHALL
throw `ArgumentNullException` for a null context, `ArgumentException` (`"Migration SQL must not be
null or empty."`) for null/whitespace SQL, and `InvalidOperationException` when the supplied context
is not a `SqlMigrationContext`.

#### Scenario: Script migration under the wrong runner

- **Given** a `SqlScriptMigration` executed through a `MongoMigrationContext`
- **When** `Up(context)` runs
- **Then** `InvalidOperationException` is thrown stating a `SqlMigrationContext` is required and that
  the migration must be run through `SqlMigrationRunner`

#### Scenario: Whitespace-only UpSql

- **Given** a subclass whose `UpSql` returns `"   "`
- **When** `Up(context)` runs
- **Then** `ArgumentException` is thrown with `paramName = "sql"`

### Requirement: CreateTablesMigration provisions tables from registered model mappings

The system SHALL, on `Up`, call `AbstractConnector.CreateTable(Type[])` for the supplied model types
and, on `Down`, call `AbstractConnector.DropTable(Type[])`. It SHALL throw `ArgumentNullException`
for a null connector, a null `tables` enumerable or a null `name`. It SHALL default `version` to `1`
and `name` to `"CreateTables"`, and SHALL report `Description` as `"Create {count} table(s) from
their registered mappings."`. Because it provisions through the connector's own connection rather
than the migration context's, it SHALL be run with `SqlMigrationSettings.UseTransaction = false`.

#### Scenario: Default version and name

- **Given** `new CreateTablesMigration(connector, new[] { typeof(Foo), typeof(Bar) })`
- **When** its properties are read
- **Then** `Version == 1`, `Name == "CreateTables"` and `Description == "Create 2 table(s) from
  their registered mappings."`

#### Scenario: Connector-driven DDL under an outer transaction

- **Given** `UseTransaction = true` and a single-writer SQLite database
- **When** `CreateTablesMigration.Up` runs
- **Then** the connector's separate connection contends with the runner transaction's write lock; the
  documented remedy is to set `UseTransaction = false` for this migration

#### Scenario: Null tables argument

- **Given** `new CreateTablesMigration(connector, null)`
- **When** the constructor runs
- **Then** `ArgumentNullException` with `paramName = "tables"` is thrown

### Requirement: TimescaleDB reuses the SQL runner with a TimescaleDB-flavoured context

The system SHALL have `TimescaleDBMigrationRunner` derive from `SqlMigrationRunner` and override
`ExecuteSingleMigration` to construct a `TimescaleDBMigrationContext(connection, transaction,
Connector)`, which is a `SqlMigrationContext` whose `ProviderName` is `"TimescaleDB"` and which
threads the PostgreSQL connector into its schema builder and data migrator.

#### Scenario: Provider name observed by a migration

- **Given** a migration run through `TimescaleDBMigrationRunner`
- **When** it reads `context.ProviderName`
- **Then** it sees `"TimescaleDB"`, whereas the same migration under `SqlMigrationRunner` sees
  `"SQL"`

### Requirement: TimescaleDB migration helpers emit hypertable and policy DDL

The system SHALL provide `TimescaleDBMigration` helpers that require a `SqlMigrationContext` —
throwing `InvalidOperationException` (`"Expected SqlMigrationContext but got {type}."`) otherwise —
and that emit: `create_hypertable(table, timeColumn[, chunk_time_interval => interval '…'])`;
`create_hypertable(table, timeColumn, spaceColumn, partitions[, …])`; `ALTER TABLE … SET
(timescaledb.compress, timescaledb.compress_orderby = '{orderByColumn}'[,
timescaledb.compress_segmentby = '{segmentByColumn}'])` followed by `add_compression_policy`;
`add_retention_policy`; `remove_compression_policy`; `remove_retention_policy`; a
`CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous)` continuous aggregate; and
`CALL refresh_continuous_aggregate(view, NULL, NULL)`. `orderByColumn` SHALL default to `"time"` and
`segmentByColumn` SHALL be omitted entirely when not supplied. All table/column/interval values SHALL
be interpolated into the SQL text, not parameterised.

#### Scenario: Compression policy without a segment-by column

- **Given** `BuildCompressionPolicySql("readings", "7 days")`
- **When** the SQL is generated
- **Then** it sets `timescaledb.compress_orderby = 'time'` and contains no
  `timescaledb.compress_segmentby` clause

#### Scenario: Continuous aggregate with an empty group-by

- **Given** `BuildContinuousAggregateSql("v", "src", "1 hour", "avg(x)", "")`
- **When** the SQL is generated
- **Then** `groupBySql` is the empty string, so both the projection and the `GROUP BY bucket` clause
  are emitted without a dangling comma

#### Scenario: Continuous aggregate always buckets on a column literally named "time"

- **Given** a source table whose timestamp column is `recorded_at`
- **When** `CreateContinuousAggregate` is used
- **Then** the generated `time_bucket('{timeBucket}', time)` still references `time`, so the
  statement fails on that table — the bucket column is not parameterised

#### Scenario: Hypertable introspection helpers omit the transaction

- **Given** a migration run with `UseTransaction = true`
- **When** `IsHypertable` or `GetChunkInterval` executes
- **Then** the command is created without assigning `command.Transaction`, unlike every DDL helper in
  the same class

### Requirement: MongoDB migrations join a session transaction only on a replica set

The system SHALL start a client session and a transaction only when
`MongoMigrationSettings.UseSession` is `true` (the default) **and**
`client.Cluster.Description.Type == ClusterType.ReplicaSet`; otherwise it SHALL execute with a null
session. It SHALL construct a fresh `MongoMigrationContext(database, session)` per migration so the
session flows into the schema builder and data migrator, SHALL commit the session after the loop,
SHALL `AbortTransaction()` inside a swallowing `try/catch` on failure, and SHALL dispose the session
in a `finally` block.

#### Scenario: Standalone MongoDB gets no transaction

- **Given** a standalone (non-replica-set) MongoDB deployment and `UseSession = true`
- **When** `Migrate()` is called
- **Then** no session is started, each operation commits immediately, and a mid-batch failure leaves
  earlier migrations applied

#### Scenario: Session threaded into every operation

- **Given** an active session
- **When** the migration calls `context.Schema.CreateCollection`, `context.Schema.DropIndex`,
  `context.Data.UpdateDocuments` or `context.Data.BulkInsert`
- **Then** each driver call is issued with the session overload, so it participates in the
  transaction

#### Scenario: Version bookkeeping does not join the session

- **Given** an active session transaction
- **When** the runner calls `store.RecordMigration(migration)` after each migration
- **Then** the store's `ReplaceOne` is issued **without** the session, so the version row commits
  immediately and survives a later `AbortTransaction()`

### Requirement: The MongoDB migration store keeps one document per version, keyed by version

The system SHALL create the collection named by `MongoMigrationSettings.MigrationsCollection`
(default `"__migrations"`) when absent, together with a **unique ascending index** on `version`.
`RecordMigration` SHALL upsert a document whose `_id` is the version rendered as a string.
`RemoveMigration` SHALL `DeleteOne` by that `_id`. Every read/write SHALL lazily call `Initialize()`
when the collection handle is still null.

#### Scenario: Re-recording the same version overwrites rather than duplicating

- **Given** version `4` already recorded
- **When** `RecordMigration` runs for version `4` again
- **Then** the `ReplaceOne` with `IsUpsert = true` replaces the existing document and `AppliedAt` is
  refreshed to `DateTime.UtcNow`

#### Scenario: Reads self-initialize

- **Given** a store on which `Initialize()` was never called
- **When** `GetAppliedVersions()` is called
- **Then** `EnsureCollectionExists()` calls `Initialize()` first, creating the collection and index if
  needed

### Requirement: The ElasticSearch migration store refreshes on write and treats a failed search as "nothing applied"

The system SHALL create the migrations index when `Indices.Exists` reports it absent, using
`ElasticSearchMigrationSettings.NumberOfShards ?? 1` and `NumberOfReplicas ?? 0` (both settings
default to `1`, so the `?? 0` arm is reached only when a caller explicitly assigns `null` — a
default-configured migrations index therefore gets **one** replica, not zero), with `Version`
mapped as a keyword and `Name`/`Description` as text and `CreatedAt`/`AppliedAt` as dates. Index name
SHALL be `MigrationsIndex` (default `"__migrations"`), prefixed with `"{Settings.Name}_"` when
`Name` is non-empty, lower-cased. `RecordMigration` SHALL index the document with `Refresh.True` and
throw `InvalidOperationException` when the response is invalid. `RemoveMigration` SHALL delete by id
with `Refresh.True`, tolerating a 404. `GetAppliedVersions` SHALL return an **empty set** both when
the index does not exist and when the search response is invalid, and SHALL read at most 10 000
documents sorted **descending** by version.

#### Scenario: Applied version immediately visible

- **Given** a just-recorded migration
- **When** `GetCurrentVersion()` is called in the same process
- **Then** the `Refresh.True` on the index call has already made the document searchable, so the new
  version is reported

#### Scenario: A failed search reports zero applied versions

- **Given** an ElasticSearch cluster that returns an invalid search response (e.g. auth failure or
  cluster red)
- **When** `GetAppliedVersions()` is called
- **Then** an empty `HashSet<long>` is returned, `GetCurrentVersion()` yields `0`, and a subsequent
  `Migrate()` would re-run every registered migration

#### Scenario: Delete of an already-absent record is tolerated

- **Given** a version whose document is not present
- **When** `RemoveMigration` runs
- **Then** the invalid response with `ServerError.Status == 404` is ignored and no exception is thrown

#### Scenario: More than 10 000 applied migrations truncate the oldest

- **Given** an index holding 10 500 migration documents
- **When** `GetAppliedVersions()` is called
- **Then** the descending sort with `Size(10000)` returns the 10 000 **highest** versions, so
  `GetCurrentVersion()` remains correct while `GetPendingMigrations()` misreports the oldest 500 as
  pending

### Requirement: ElasticSearch migrations have no transaction

The system SHALL execute each ElasticSearch migration against a fresh
`ElasticSearchMigrationContext(client)` and record its version immediately, with no batch-level
atomicity, and SHALL wrap any failure in a `MigrationException` stating the state may be
inconsistent.

#### Scenario: Mid-batch failure leaves earlier migrations recorded

- **Given** a three-migration batch whose third `Up` throws
- **When** `Migrate()` is called
- **Then** versions 1 and 2 are already recorded in the migrations index, and the exception blames
  migration 3

### Requirement: The RavenDB migration store keeps all applied versions in one configurable state document

The system SHALL store migration state as a single `MigrationsStateDocument` whose id is
`RavenMigrationSettings.MigrationsDocumentId` (default `"Migrations/State"`), holding a dictionary
keyed by the version's string form. `Initialize()` SHALL create the document when absent.
`RecordMigration` SHALL load-or-construct the document, set the record and `SaveChanges()`.
`RemoveMigration` SHALL remove the key and save only when the document and dictionary exist. Reads
SHALL open a fresh session each time and SHALL return an empty set when the document or its
dictionary is null.

#### Scenario: Two modules share one Raven database

- **Given** two runners configured with `MigrationsDocumentId = "Migrations/State/IoT"` and
  `"Migrations/State/Events"`
- **When** each records its own migrations
- **Then** the two state documents are independent and neither module sees the other's versions

#### Scenario: Concurrent recording is last-write-wins

- **Given** two processes recording different versions at the same time
- **When** both load the state document, mutate it and `SaveChanges()`
- **Then** the document is replaced wholesale with no optimistic-concurrency check, so one process's
  version can be lost

### Requirement: The CosmosDB migration store keeps one state item in a dedicated container

The system SHALL, on `Initialize()`, `CreateContainerIfNotExistsAsync` a container named
`CosmosMigrationSettings.MigrationsContainerName` (default `"Migrations"`) with the partition-key
path hard-coded to `"/partitionKey"`, then read the state item with id
`MigrationsDocumentId` (default `"Migrations-State"`) and partition key `MigrationsPartitionKey`
(default `"migrations"`), creating it when the read raises `CosmosException` with
`HttpStatusCode.NotFound`. `GetAppliedVersions` SHALL return an empty set on `NotFound`.
`RecordMigration` and `RemoveMigration` SHALL re-read the state item, mutate the dictionary and
`ReplaceItemAsync`. All calls SHALL be made synchronously via `.GetAwaiter().GetResult()`.

#### Scenario: First run creates container and state item

- **Given** an empty Cosmos database
- **When** `Initialize()` runs
- **Then** the container is created and the initial state item with an empty `AppliedMigrations`
  dictionary is inserted

#### Scenario: Async store members do not perform async I/O

- **Given** `RecordMigrationAsync(migration, token)`
- **When** it is awaited
- **Then** it observes the token once via `ThrowIfCancellationRequested()`, calls the synchronous
  `RecordMigration`, and returns `Task.CompletedTask`

### Requirement: The InfluxDB migration store tracks versions as points in a fixed bucket

The system SHALL use the hard-coded bucket `"_migrations"` and measurement `"migrations"` — there is
no settings class for the InfluxDB migration backend, and the organization is supplied to the runner
constructor. `Initialize()` SHALL find the bucket by case-insensitive name or create it with a
365-day expiry retention rule. `GetAppliedVersions()` SHALL run a Flux query over `range(start:
-10y)` filtered to `_field == "version"` with `distinct`, parse each `_value` with
`long.TryParse`, and SHALL swallow any `InfluxException`, returning whatever versions were collected.
`RecordMigration` SHALL write a point via the non-batching `GetWriteApiAsync()` with the migration
name as a tag, `version` and `description` as fields, and `CreatedAt` as the millisecond timestamp.
`RemoveMigration` SHALL delete over `[CreatedAt - 1 minute, UtcNow]` with the predicate
`_measurement="migrations" AND name="{escaped name}"`, swallowing `InfluxException`.

#### Scenario: A query failure is indistinguishable from an empty bucket

- **Given** an InfluxDB server that rejects the version query with an `InfluxException` (e.g. bad
  token)
- **When** `GetAppliedVersions()` is called
- **Then** the exception is swallowed and an empty set is returned, so `GetCurrentVersion()` yields
  `0` and every migration is considered pending

#### Scenario: Flux string values are escaped

- **Given** a migration whose `Name` is `he said "hi"\`
- **When** `RemoveMigration` builds its predicate
- **Then** `EscapeFluxString` backslash-escapes the backslashes first and then the double quotes, so
  the predicate remains a valid Flux string literal

#### Scenario: Non-Influx exceptions propagate

- **Given** a programming error inside the version query loop that raises `NullReferenceException`
- **When** `GetAppliedVersions()` runs
- **Then** the exception propagates rather than being read as "no applied versions"

### Requirement: A migration context exposes schema, data, provider name and a raw escape hatch

The system SHALL give every migration an `IMigrationContext` with a non-null `Schema`
(`ISchemaBuilder`), a non-null `Data` (`IDataMigrator`), a `ProviderName` string, and `Raw(Action<object>)`
which invokes the supplied action with the backend's native handle. Constructors SHALL throw
`ArgumentNullException` for a null handle.

#### Scenario: Raw hands back the native client per backend

- **Given** a migration calling `context.Raw(o => …)`
- **When** each backend's context is used
- **Then** the action receives a `DbConnection` (SQL and TimescaleDB), an `IMongoDatabase`, a Nest
  `ElasticClient`, a RavenDB `IDocumentStore`, a Cosmos `Database`, or an `InfluxDBClient`

#### Scenario: Provider names

- **Given** each backend context
- **When** `ProviderName` is read
- **Then** it returns `"SQL"`, `"TimescaleDB"`, `"MongoDB"`, `"ElasticSearch"`, `"RavenDB"`,
  `"CosmosDB"` or `"InfluxDB"` respectively

### Requirement: Collection and index builders are terminal-operation based, and only some backends implement the terminal

The system SHALL define `ICollectionBuilder.Build()` and `IIndexBuilder.Build()` as default no-op
interface methods that a migration must call to finish a `CreateCollection(...).WithField(...)` or
`CreateIndex(...).WithField(...)` chain. Only the SQL builders override **both** terminals; the
MongoDB index builder overrides `IIndexBuilder.Build()`; the ElasticSearch, RavenDB, CosmosDB and
InfluxDB index builders and the MongoDB/ElasticSearch/RavenDB/CosmosDB/InfluxDB collection builders
do **not** override `Build()`.

#### Scenario: SQL collection chain must be terminated

- **Given** a SQL migration calling `context.Schema.CreateCollection("Foo").WithField("Id",
  FieldType.Guid, isPrimary: true)` and never calling `Build()`
- **When** the migration completes
- **Then** no `CREATE TABLE` is emitted — the accumulated definition is discarded

#### Scenario: SQL Build is idempotent

- **Given** a `SqlCollectionBuilder` or `SqlIndexBuilder` whose `Build()` has already run
- **When** `Build()` is called again
- **Then** the `_built` flag short-circuits and no second statement is executed

#### Scenario: SQL index with no fields

- **Given** `context.Schema.CreateIndex("Foo", "IX_Foo")` with no `WithField` call
- **When** `Build()` is called
- **Then** `InvalidOperationException` (`"Index must have at least one field."`) is thrown

#### Scenario: ElasticSearch, RavenDB, CosmosDB and InfluxDB CreateIndex create nothing

- **Given** a migration calling `context.Schema.CreateIndex("things", "byName").WithField("name").Build()`
  on any of those four backends
- **When** the migration completes
- **Then** the default no-op `Build()` runs and no index is created; `Unique()` is recorded only in
  an `internal bool IsUnique` (ElasticSearch, RavenDB, CosmosDB) or discarded entirely (InfluxDB)

#### Scenario: MongoDB index is created on Build

- **Given** `context.Schema.CreateIndex("things", "byName").WithField("name").Unique().Build()`
- **When** the migration runs
- **Then** a `CreateIndexModel` with `Name = "byName"` and `Unique = true` is created (using the
  session when one is active); with zero fields, `Build()` returns without creating anything

### Requirement: The SQL schema builder requires a connector and has no raw-SQL fallback

The system SHALL require a non-null `AbstractConnector` in `SqlSchemaBuilder`, `SqlMigrationContext` and
`SqlDataMigrator`, throwing `ArgumentNullException` otherwise, and SHALL route `DropCollection`,
`DropIndex`, `AddField`, `DropField`, `CreateCollection(...).Build()` and `CreateIndex(...).Build()`
through that connector after calling `SetExternalTransaction(connection, transaction)`. Identifiers
SHALL be quoted via `connector.QuoteIdentifier`. `RenameField` SHALL use raw
`ALTER TABLE … RENAME COLUMN … TO …` because no connector equivalent exists, but SHALL quote through
the connector's dialect. `CollectionExists` SHALL query `sqlite_master` when the connection type name
contains `"Sqlite"` and `INFORMATION_SCHEMA.TABLES` otherwise.

#### Scenario: A null connector is refused, and the message names the alternative

- **Given** `new SqlSchemaBuilder(connection, transaction, null)`
- **When** the constructor runs
- **Then** `ArgumentNullException` is thrown, and its message states that the removed raw-SQL fallback
  was wrong on MySQL and PostgreSQL and that `SqlMigrationRunner` already holds a connector
- **And** `SqlMigrationContext`'s `connector` parameter is required at compile time, so the optional
  argument that was the only door to the fallbacks no longer exists

#### Scenario: Every schema operation delegates to the provider's own emitter

- **Given** a connector-backed builder
- **When** `DropCollection` / `AddField` / `DropField` / `CreateCollection(...).Build()` /
  `CreateIndex(...).Build()` / `DropIndex` run
- **Then** each calls `connector.DropTable` / `AlterTableAdd` / `AlterTableDrop` /
  `FieldDefinition`+`CreateTable` / `CreateIndexes` / `DropIndexes` respectively — there is no
  branch on whether a connector was supplied, so index and column DDL has one producer per dialect

#### Scenario: A composite primary key is not expressible through this builder

- **Given** `CreateCollection(...)` with two fields marked primary
- **When** `Build()` runs
- **Then** `AbstractConnector.CreateTable` renders `PRIMARY KEY` per column from each field's flag; no
  composite `PRIMARY KEY (a, b)` clause is emitted. The deleted fallback did emit one from
  `_primaryKeyFields`, so this is a capability the removal dropped deliberately — nothing in the
  repository or any consumer declares one this way

#### Scenario: SQLite existence check

- **Given** a `SqliteConnection`
- **When** `CollectionExists("Foo")` is called
- **Then** `SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @tableName` is run,
  not the `INFORMATION_SCHEMA` variant which does not exist in SQLite

#### Scenario: Column types come from the provider, not a portable table

- **Given** a connector-backed `AddField` or `CreateCollection(...).Build()`
- **When** the DDL is emitted
- **Then** the column type comes from the connector's `FieldDefinition` / `ConvertType` for that
  dialect. The builder carries no type table of its own: `FieldTypeToSql`, `FormatValue` and
  `FormatColumn` were deleted with the fallback that was their only caller, so a provider-specific
  mapping (for example MySQL bounding an indexed string to `VARCHAR(255)`) applies here too rather
  than being approximated

#### Scenario: DropField discards the real field type

- **Given** a connector-backed `DropField(collection, "Age")`
- **When** the `SchemaField` is constructed for the connector call
- **Then** it is built from `new FieldDescriptor { Name = fieldName, Type = FieldType.String }`
  regardless of the column's actual type

#### Scenario: DropIndex is per-dialect, and the non-portable fallback is gone

- **Given** a connector-backed `DropIndex("Foo", "IX_Foo")`
- **When** it runs
- **Then** the statement comes from that connector's `DropIndexSql` — `DROP INDEX "IX_Foo"` with an
  `IF EXISTS` where the dialect supports it, and ``DROP INDEX `IX_Foo` ON `Foo` `` on MySQL, which
  accepts no `IF EXISTS` but requires the `ON` clause
- **And** the deleted fallback emitted `DROP INDEX IF EXISTS "IX_Foo" ON "Foo"`, a single form that is
  invalid on MySQL (for the `IF EXISTS`) and on PostgreSQL (for the `ON`) — wrong on both in opposite
  directions, which is why the connector-free path was removed rather than repaired

#### Scenario: A migration's index column name is validated before it reaches the statement

- **Given** `CreateIndex("Docs", "ix").WithField("Rank); CREATE TABLE Pwned (x INTEGER); --")`
- **When** `WithField` runs
- **Then** `ArgumentException` is thrown by `DataBase.ValidateIndexFieldIdentifier`, because index
  columns are interpolated **bare** into `CREATE INDEX` and this name arrives from the caller. The
  check is validated at the declaration site so it covers the whole builder, and it rejects a
  `Table.Column` qualifier too, which is invalid in an index column list

#### Scenario: A declared unique index is actually unique

- **Given** `CreateIndex("Orders", "ux").WithField("TenantGuid").WithField("Number").Unique().Build()`
- **When** the index is created
- **Then** the emitted statement carries `UNIQUE` and the engine enforces it. `Build()` copies
  `_unique` onto the `Tables.IndexDefinition` it hands the connector; before that copy existed the
  declared constraint was silently absent on every provider

### Requirement: Document backends treat schema operations as no-ops or index-policy edits

The system SHALL implement `AddField`, `DropField` and `DropIndex` as **no-ops** for MongoDB
(`AddField`, `DropField`), ElasticSearch (`AddField`, `DropField`, `DropIndex`), RavenDB (`AddField`,
`DropField`) and InfluxDB (`AddField`, `DropField`, `DropIndex`, `RenameField`). For CosmosDB,
`AddField` SHALL add `/{field}/?` to the container's indexing-policy included paths and `DropField`
SHALL add it to the excluded paths, replacing the container only when the path is not already
present. `DropIndex` SHALL delete the named Raven index (`DeleteIndexOperation`) and SHALL be a
no-op for CosmosDB.

#### Scenario: Renaming a field is emulated per backend

- **Given** `context.Schema.RenameField("things", "old", "new")`
- **When** each backend runs it
- **Then** SQL emits `ALTER TABLE … RENAME COLUMN`; MongoDB issues `UpdateMany` with
  `$rename` over an empty filter; ElasticSearch issues `_update_by_query` with the Painless source
  `ctx._source.new = ctx._source.remove('old')`; RavenDB issues a `PatchByQueryOperation` with `FROM
  'things' UPDATE { this.new = this.old; delete this.old; }`; CosmosDB queries every document where
  `IS_DEFINED(c["old"])` and point-patches each with `Set /new` + `Remove /old`; InfluxDB does
  nothing at all

#### Scenario: CosmosDB rename tolerates only absent-field and absent-document errors

- **Given** a Cosmos patch that fails with `HttpStatusCode.BadRequest` or `NotFound`
- **When** `RenameField` processes that document
- **Then** the failure is swallowed so the rename stays idempotent; a 429 (throttling), 401/403
  (auth) or 503 propagates

#### Scenario: CosmosDB rename patches on the real partition key

- **Given** a container whose partition-key path is `/tenantId`
- **When** `RenameField` runs
- **Then** the projection additionally selects `c.tenantId` and the point patch uses a partition key
  built from that value via `CosmosDBDataMigrator.BuildPartitionKey`, not from the document id

#### Scenario: ElasticSearch rename result is not validated

- **Given** an `_update_by_query` that the cluster rejects
- **When** `ElasticSearchSchemaBuilder.RenameField` runs
- **Then** the response is discarded without an `IsValid` check, so the migration is recorded as
  applied

### Requirement: Collection creation and dropping differ sharply across backends

The system SHALL implement `CreateCollection`/`DropCollection` as follows. SQL: `CreateCollection`
returns a builder that emits `CREATE TABLE` on `Build()`; `DropCollection` calls
`connector.DropTable` or falls back to `DROP TABLE IF EXISTS`. MongoDB: `CreateCollection`
eagerly calls `database.CreateCollection` before returning the builder; `DropCollection` drops the
collection. ElasticSearch: `CreateCollection` creates the index with 1 shard / 0 replicas when
absent; `DropCollection` deletes it when present. RavenDB: `CreateCollection` creates nothing (Raven
collections are implicit) and `DropCollection` issues a `DeleteByQueryOperation` for `FROM
'{name}'`, waiting for completion. CosmosDB: `CreateCollection` calls
`CreateContainerIfNotExistsAsync` with the partition-key path `"/id"` and `DropCollection` deletes
the container. InfluxDB: `CreateCollection` creates a bucket with no retention rule when absent and
`DropCollection` deletes the bucket.

#### Scenario: RavenDB DropCollection empties rather than removes

- **Given** a Raven collection `Orders` with documents
- **When** `DropCollection("Orders")` runs
- **Then** all documents matching `FROM 'Orders'` are deleted and the operation is awaited; the
  collection name simply stops appearing once it holds no documents

#### Scenario: CosmosDB partition key cannot be chosen through the builder

- **Given** `context.Schema.CreateCollection("Orders").WithField("tenantId", FieldType.String,
  isPrimary: true)`
- **When** the chain runs
- **Then** the container was already created with partition-key path `"/id"` by `CreateCollection`,
  the builder records `_partitionKeyPath = "/tenantId"` in a private field, and no `Build()` override
  exists to apply it — so the requested partition key is silently ignored

#### Scenario: RavenDB CollectionExists means "holds at least one document"

- **Given** an existing Raven collection that currently has zero documents
- **When** `CollectionExists(name)` is called
- **Then** it returns `false`, because it reads `GetCollectionStatisticsOperation` and requires
  `count > 0`

#### Scenario: ElasticSearch collection settings ignore the migration settings

- **Given** `ElasticSearchMigrationSettings.NumberOfShards = 5`
- **When** a migration calls `context.Schema.CreateCollection("things")`
- **Then** the index is still created with `NumberOfShards(1)` and `NumberOfReplicas(0)` — the schema
  builder never receives the settings object

### Requirement: The data migrator's filter argument is a Mongo-style JSON object translated per backend

The system SHALL accept `filterJson` as a JSON object whose properties are field names. A scalar
value SHALL mean equality. An object value SHALL be read operator-by-operator, honouring `$gt`,
`$gte`, `$lt`, `$lte` and `$ne`, and SHALL translate **any other operator name to equality**. Values
SHALL be extracted as string / `long` (falling back to `double`) / `bool` / `null`, with arrays and
nested objects degraded to their raw JSON text via `ToString()`. Multiple conditions SHALL be
combined with AND. A null, whitespace or `"{}"` filter SHALL mean "no restriction".

#### Scenario: Range and equality combined

- **Given** `filterJson = {"status":"active","age":{"$gte":18,"$lt":65}}`
- **When** it is translated
- **Then** SQL yields `"status" = @p0 AND "age" >= @p1 AND "age" < @p2`; CosmosDB yields
  `c["status"] = 'active' AND c["age"] >= 18 AND c["age"] < 65`; RavenDB yields `status = $p0 AND age
  >= $p1 AND age < $p2` with RQL query parameters; ElasticSearch yields a `bool.must` of a `TermQuery`
  and two `NumericRangeQuery` clauses; MongoDB passes the document through to the driver unchanged

#### Scenario: An unsupported operator becomes equality

- **Given** `filterJson = {"tags":{"$in":["a","b"]}}`
- **When** it is translated by the SQL, ElasticSearch, RavenDB or CosmosDB migrator
- **Then** the `$in` falls into the `_ => "="` / default arm and produces an equality comparison
  against the array's JSON text (`["a","b"]`), matching nothing, rather than raising an error

#### Scenario: Empty filter means every document

- **Given** `filterJson` null, `""` or `"{}"`
- **When** `DeleteDocuments` is called
- **Then** SQL emits `DELETE FROM {table}` with no `WHERE`; MongoDB uses `Filter.Empty`;
  ElasticSearch uses `MatchAll`; CosmosDB emits no `WHERE`; RavenDB emits `FROM '{collection}'` with
  no `WHERE` — every backend deletes the whole collection

#### Scenario: ElasticSearch rejects a non-numeric range bound

- **Given** `filterJson = {"age":{"$gt":null}}`
- **When** the ElasticSearch migrator translates it
- **Then** `ToRangeBound` throws `ArgumentException` (`"Range operator '$gt' on field 'age' requires a
  non-null numeric value."`) instead of silently becoming `> 0`

#### Scenario: ElasticSearch range operators are always numeric

- **Given** `filterJson = {"createdAt":{"$gt":"2026-01-01"}}`
- **When** the ElasticSearch migrator translates it
- **Then** `ToRangeBound` attempts `Convert.ToDouble("2026-01-01")` and throws `ArgumentException`
  — date ranges are not expressible through this dialect

#### Scenario: SQL $ne against null never matches

- **Given** `filterJson = {"deletedAt":{"$ne":null}}`
- **When** the SQL migrator translates it
- **Then** it emits `"deletedAt" <> @p0` with the parameter bound to `DBNull.Value`, a predicate that
  is never true in SQL, rather than `IS NOT NULL`

#### Scenario: InfluxDB refuses JSON filters outright

- **Given** any `filterJson` beginning with `{` other than `{}`
- **When** `InfluxDBDataMigrator.DeleteDocuments` translates it
- **Then** `NotSupportedException` is thrown telling the caller to pass a Flux delete predicate
  string; a non-`{`-prefixed string is passed through to Influx verbatim

### Requirement: Update and delete of documents are parameterised or explicitly unsupported

The system SHALL return without doing anything when `updates` is null or empty. SQL SHALL emit a
single `UPDATE … SET col = @pN` statement with dialect-quoted identifiers. MongoDB SHALL issue one
`UpdateMany` with `$set`. ElasticSearch SHALL issue `_update_by_query` with a Painless script that
assigns each value from a script parameter (`ctx._source.field = params.pN`) and SHALL throw when the
response is invalid. RavenDB SHALL issue a `PatchByQueryOperation` with `$uN` query parameters and
wait for completion. CosmosDB SHALL query matching ids (plus the partition-key field), then issue one
`PatchItemAsync` per document. InfluxDB `UpdateDocuments` SHALL be a **silent no-op**.

#### Scenario: InfluxDB update silently does nothing

- **Given** `context.Data.UpdateDocuments("readings", "{}", new Dictionary<string, object> { ["x"] = 1 })`
- **When** the migration runs on InfluxDB
- **Then** no write is attempted, no exception is thrown, and the migration is recorded as applied

#### Scenario: ElasticSearch update values survive quoting

- **Given** `updates = { ["note"] = "he said \"hi\"", ["flag"] = true }`
- **When** `BuildPainlessSource` runs
- **Then** the script source is `ctx._source.note = params.p0; ctx._source.flag = params.p1` and the
  values are carried as serialised script params, so quotes/backslashes/bools/dates are not broken by
  string formatting

#### Scenario: CosmosDB update is per-document, not server-side

- **Given** a filter matching 10 000 Cosmos documents
- **When** `UpdateDocuments` runs
- **Then** 10 000 individual `PatchItemAsync` calls are issued sequentially and awaited one at a time

#### Scenario: ElasticSearch delete failure is not swallowed

- **Given** a `_delete_by_query` that returns an invalid response
- **When** `DeleteDocuments` runs
- **Then** `EnsureValid` throws `InvalidOperationException` including the operation description and
  `DebugInformation`, so the migration is not recorded as applied

### Requirement: CopyData behaviour diverges by backend, from honoured transform to hard refusal

The system SHALL implement `CopyData(source, target, transformJson)` as follows. **MongoDB** SHALL
build an aggregation pipeline of the caller's transform stage(s) followed by `$merge` into the target,
accepting either a single stage object or a JSON array of stages and treating null/empty/`"{}"` as a
straight copy. **ElasticSearch** SHALL throw `NotSupportedException` when `transformJson` is
non-empty, and otherwise `ReindexOnServer` with `WaitForCompletion(true)` followed by a refresh of
the target. **RavenDB** SHALL **always** throw `NotSupportedException` — the operation is
unimplemented. **SQL** SHALL emit `INSERT INTO {target} SELECT * FROM {source}` and **silently
ignore** `transformJson`. **CosmosDB** SHALL page every source document and upsert it into the target
container, **silently ignoring** `transformJson`. **InfluxDB** SHALL query the source bucket over
`range(start: -100y)`, rebuild each record as a point tagged `_original_bucket = source`, preserve the
original timestamp, and write the points to the target bucket, **silently ignoring** `transformJson`.

#### Scenario: MongoDB honours an array of transform stages

- **Given** `transformJson = "[{\"$match\":{\"active\":true}},{\"$project\":{\"name\":1}}]"`
- **When** `BuildCopyPipeline("target", transformJson)` runs
- **Then** the returned pipeline is the two supplied stages followed by `{"$merge":{"into":"target"}}`

#### Scenario: RavenDB copy always fails fast

- **Given** any call to `RavenDBDataMigrator.CopyData`
- **When** it executes
- **Then** `NotSupportedException` is thrown explaining that a cross-collection copy must re-key
  documents into the target collection

#### Scenario: SQL copy drops a requested transform without warning

- **Given** `context.Data.CopyData("Old", "New", "{\"$set\":{\"x\":1}}")` on SQL
- **When** the migration runs
- **Then** `INSERT INTO "New" SELECT * FROM "Old"` is executed, the transform is discarded and no
  exception is raised

#### Scenario: ElasticSearch copy rejects a transform instead of dropping it

- **Given** the same call on ElasticSearch
- **When** the migration runs
- **Then** `NotSupportedException` is thrown before any reindex is attempted

### Requirement: Document counting honours the collection and filter except on InfluxDB

The system SHALL count documents as follows. SQL: `SELECT COUNT(*) FROM {table}` plus the translated
`WHERE`. MongoDB: `CountDocuments(filter)` (session-aware). ElasticSearch: a `Count` request with the
translated query, returning `response.Count` **without validating the response**. RavenDB: the
collection-scoped `DocumentQuery` with the filter applied, executed with `Take(0)` and read from
`QueryStatistics.TotalResults`. CosmosDB: `SELECT VALUE COUNT(1) FROM c` plus the translated `WHERE`.
InfluxDB: a Flux `count()`/`group()`/`sum()` over `range(start: -100y)` with `filter(fn: (r) => true)`
that **ignores `filterJson` entirely**.

#### Scenario: InfluxDB count ignores the filter

- **Given** `context.Data.CountDocuments("readings", "{\"host\":\"a\"}")`
- **When** it runs
- **Then** the Flux query filters on `true`, so the total number of records in the bucket is returned
  regardless of the requested filter

#### Scenario: ElasticSearch count of a missing index returns zero

- **Given** an index that does not exist
- **When** `CountDocuments` runs
- **Then** the invalid response's `Count` (0) is returned with no exception, so a migration guarded by
  `if (Count == 0)` proceeds as though the collection were empty

#### Scenario: RavenDB count is scoped to the collection

- **Given** a database with documents in several collections
- **When** `CountDocuments("Orders")` runs
- **Then** the count comes from the `Orders`-scoped `DocumentQuery`'s statistics, not from a
  database-wide query

### Requirement: Bulk insert places documents in the requested collection

The system SHALL skip null and empty documents in every backend. SQL SHALL issue one parameterised
`INSERT INTO {table} (cols) VALUES (@p0, …)` per document. MongoDB SHALL `InsertMany` the converted
`BsonDocument`s (session-aware) only when at least one survives filtering. ElasticSearch SHALL
build one `BulkDescriptor`, validate the response, throw `InvalidOperationException` when
`bulkResponse.Errors` is set (reporting the first item error), and refresh the index. RavenDB SHALL
force the target collection by setting the `@collection` metadata on each document in a
`BulkInsert` operation. CosmosDB SHALL derive each item's id from an `"id"` key or a new `Guid`, build
the partition key from the container's real partition-key property (falling back to the id), and issue
all `CreateItemAsync` calls concurrently via `Task.WhenAll`. InfluxDB SHALL derive the measurement
from a `_measurement` key (default `"migration_data"`), skip keys starting with `_` and the key
`"time"`, and honour a `DateTime` `_time`/`time` value as the point timestamp.

#### Scenario: RavenDB bulk insert lands in the named collection

- **Given** `BulkInsert("Orders", documents)` where each document is a raw `IDictionary<string, object>`
- **When** the insert runs
- **Then** each document is stored with `@collection = "Orders"` metadata rather than a collection
  derived from the CLR dictionary type

#### Scenario: ElasticSearch partial bulk failure is surfaced

- **Given** a bulk request where the response is valid but `Errors` is true
- **When** `BulkInsert` runs
- **Then** `InvalidOperationException` is thrown quoting the first item error's `Reason`

#### Scenario: InfluxDB value typing is preserved

- **Given** a document with a string, a bool, an `int`, a `decimal` and a `DateTime` value
- **When** `ApplyValue` maps each
- **Then** the string becomes a **tag**, the bool / integral / floating-point values become typed
  fields, `null` is skipped, and anything else (including `DateTime` and `byte[]`) is written as its
  `ToString()` representation

#### Scenario: All backends skip empty documents

- **Given** a sequence containing `null` and an empty dictionary alongside two real documents
- **When** `BulkInsert` runs on any backend
- **Then** only the two real documents are written, and a fully empty sequence results in no write at
  all

### Requirement: MigrationResult records the transition and the executed set

The system SHALL expose `Success`, `FromVersion`, `ToVersion`, `Direction`, `ExecutedMigrations`
(defaulting to an empty array), `ErrorMessage` and `Exception` on `MigrationResult`.
`Successful(from, to, direction, executed)` SHALL set `Success = true` and leave `ErrorMessage` /
`Exception` null. `Failed(from, direction, message, exception)` SHALL set `Success = false` and
`ToVersion = FromVersion`. Each `ExecutedMigration` SHALL carry the `IMigration`, the `Direction` and
an `ExecutedAt` stamped `DateTime.UtcNow` at construction.

#### Scenario: A failed guard reports no movement

- **Given** `MigrationResult.Failed(5, MigrationDirection.Up, "…")`
- **When** the result is inspected
- **Then** `FromVersion == ToVersion == 5` and `ExecutedMigrations` is empty

#### Scenario: ToVersion reflects the request, not the highest applied version

- **Given** registered versions `{1, 10}`, current version `1`, and `Migrate(7)`
- **When** the successful result is inspected
- **Then** `ToVersion == 7` although the highest actually applied version is still `1`

### Requirement: MigrationException carries migration context

The system SHALL provide `MigrationException` constructors for a bare instance, a message, a
message plus inner exception, an `(IMigration, MigrationDirection, string, Exception?)` form, and an
`(IMigration, MigrationDirection, Exception)` form whose message is `"Migration {Version} ({Name})
failed during {Direction}."`. `Migration` SHALL be null when a message-only constructor is used, and
`Direction` SHALL then default to `MigrationDirection.Up`.

#### Scenario: Auto-generated message

- **Given** `new MigrationException(migrationV7NamedAddIndex, MigrationDirection.Down, inner)`
- **When** `Message` is read
- **Then** it is `"Migration 7 (AddIndex) failed during Down."`

#### Scenario: Message-only construction loses attribution

- **Given** `new MigrationException("Migration transaction failed to commit. …", inner)`
- **When** the exception is inspected
- **Then** `Migration` is null and `Direction` is the enum default `Up`, even if the failing batch was
  a rollback
