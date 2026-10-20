# Birko.Data.Localization.SQL

## Overview
SQL schema for `Birko.Data.Localization`'s translation rows (TASK-680, EPIC-021): a fluent `IModelMapping` that gives
any `IEntityTranslation` model its table and the **unique key** `(EntityGuid, EntityType, FieldName, Culture)`.
Schemaless backends (InMemory, JSON, XML, Mongo, Raven, Cosmos, ES) need none of this. SQL does, because
`Birko.Data.SQL` builds columns only from SQL attributes or a fluent mapping, and `Birko.Data.Localization` stays free of
SQL by design. Follows `Birko.Security.Grants.SQL`.

## Project Location
`Birko.Data.Localization.SQL/`

## Components
```
Birko.Data.Localization.SQL/
├── LocalizationSqlMappings.cs            — Register(registry, table) for EntityTranslationModel;
│                                           Register<TTranslation>(registry, table) for a consumer's model
├── Mappings/EntityTranslationMapping.cs  — table (default BirkoEntityTranslations), Guid primary, four required key
│                                           columns + UX_{table}_Key (unique), Value required; no index on Value
└── SqlLocalizedWriteBoundary.cs          — For(entityStore, translationStore, requireAtomic): atomic when both chains
                                            reach an ISqlConnectedStore with one Settings.GetId(), best effort otherwise
                                            (TASK-683)
```

## Dependencies
- `Birko.Data.Localization` (`IEntityTranslation`, `EntityTranslationModel`), `Birko.Models.SQL` (fluent mapping),
  `Birko.Data.SQL` (via the mapping registry)

## Rules the code keeps
- **The unique key is what makes one row per key true** (SH-M074). The wrappers' upsert reads, then creates; a create
  the key refuses (`StoreUniqueViolationException`) is retried as an update. Measured live: one row on SQLite,
  PostgreSQL and SQL Server; without the index, two.
- **No index on `Value`** (decided by the user, TASK-680): an index key bounds the column to 255 characters on MySQL and
  SQL Server (rule 37), capping every translation. Exact-value lookups use `IX_{table}_ValueHash` on `ValueHash` (SHA-256
  hex, 64 characters) and confirm the value in memory (TASK-681). The table name is at most 50 characters so both index
  names fit PostgreSQL's 63.
- **The key's string columns are bounded** (TASK-684): `EntityType` and `FieldName` 128, `Culture` 85 (decided by the
  user), through the fluent `HasMaxLength` TASK-691 made reach the DDL. Left at rule 37's 255, the key was 3 204 bytes
  under MySQL's utf8mb4, over InnoDB's 3 072 (ERROR 1071, measured), and MySQL could not hold it. Bounded it is 1 508
  bytes on MySQL and 698 on SQL Server. Measured live: the key exists on all four providers, the catalogue reports the
  lengths, and a longer `Culture` is refused by MySQL, PostgreSQL and SQL Server (SQLite does not enforce lengths).
- **A table created before TASK-684 keeps its VARCHAR(255) columns.** On MySQL its key stays refused and is reported in
  `IndexCreationFailures` on every init. On every provider `DetectDrift` names the three columns, so
  `SchemaDriftHealthCheck` reads Degraded until they are narrowed. The README gives the remedy per provider.
- **MySQL refuses an over-long value only under a strict `sql_mode`;** without it, MySQL truncates.
- **Inside a caller's transaction, a key race is not retried in place on PostgreSQL or MySQL** (measured). PostgreSQL
  aborts the transaction. MySQL keeps it, but the re-read runs on the transaction's REPEATABLE READ snapshot and finds
  no row. SQL Server retries in place. An owned `SqlLocalizedWriteBoundary` re-runs the whole write (TASK-683).
- A changed mapping is not applied to an existing table: schema-ensure never alters an index or column.
- **One database means one settings id** (`Connector.Settings.GetId()`), the key `AmbientSqlTransaction` itself uses,
  never connector identity (rule 16).
- **The boundary joins before it opens.** Async: a `SqlUnitOfWork` built in the flow that runs the write. Sync: it
  mirrors `DataBaseBulkStore.Atomically`, in a cell of its own. A failure as a participant marks the caller's boundary
  rollback-only. `EntityNotFoundException` commits the stored part (TASK-555).
- **The PostgreSQL race is retried by re-running the whole write once** (decided by the user, TASK-683). Only when the
  boundary owns the transaction: a refused insert aborts it, so the in-place upsert retry cannot re-read. Inside a
  caller's transaction the typed violation surfaces. Measured: removing the retry fails the PostgreSQL race test, and
  SQL Server retries in place.

## Maintenance
See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Tests: `tests/Birko.Data.Localization.SQL.Tests`
(SQLite always; PostgreSQL, SQL Server and MySQL with `BIRKO_*_HOST`, per `live-tests.yml`).
