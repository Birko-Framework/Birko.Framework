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
- **MySQL cannot hold the key yet**: four columns of VARCHAR(255) utf8mb4 are 3 204 bytes, over InnoDB's 3 072 (ERROR
  1071, measured). Schema-ensure reports it (`IndexCreationFailures`, rule 49); bounding the columns needs a fluent
  length the mapping registry does not apply — TASK-684.
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
