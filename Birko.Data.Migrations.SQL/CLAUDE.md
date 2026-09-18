# Birko.Data.Migrations.SQL

## Overview
SQL migration backend using AbstractConnector from Birko.Data.SQL. Implements platform-agnostic IMigrationContext for SQL databases.

## Project Location
`C:\Source\Birko.Data.Migrations.SQL\`

## Components

### Runner
- `SqlMigrationRunner` — Takes `AbstractConnector` (from `store.Connector`), creates SqlMigrationContext per migration

### Context
- `SqlMigrationContext` — Wraps DbConnection + DbTransaction + optional AbstractConnector. Schema and Data properties. Raw() exposes DbConnection.
- `SqlSchemaBuilder` — Uses AbstractConnector when available (CreateTable, AlterTable, CreateIndexes). Falls back to raw SQL DDL. FieldType to SQL type mapping via SchemaField.
- `SqlDataMigrator` — JSON filter parsing with System.Text.Json, parameterized queries for update/delete/count.

### Store
- `SqlMigrationStore` — Stores migration state in a SQL table. Takes connection factory + settings.

### Settings
- `SqlMigrationSettings` — extends `SqlSettings` (Birko.Data.SQL). Adds `UseTransaction`, `MigrationsTable`, `SchemaName`. Inherits `CommandTimeout`, `ConnectionTimeout` from `SqlSettings`.

### Internal
- `SchemaField` — Extends AbstractField with null PropertyInfo (DDL-only). Maps FieldType to DbType.

### Migration bases
- `CreateTablesMigration` — mapping-driven table provisioning from registered `IModelMapping<T>` (run with `UseTransaction = false`; drives the connector's own connection).
- `SqlScriptMigration` — raw-SQL/DDL base. A subclass supplies `UpSql` (required) and optionally `DownSql` (null → base `NotImplementedException`); the base runs each script against the migration context's `Connection`/`Transaction` (one `ExecuteNonQuery`), so consumers no longer cast `IMigrationContext`→`SqlMigrationContext` or hand-roll connection/command plumbing. Runs cleanly under the default `UseTransaction = true`. SQLite executes multi-statement `;`-separated batches; some other providers execute only the first statement per command — split across migrations or override `Execute`.

## Usage

```csharp
var runner = new SqlMigrationRunner(store.Connector);
runner.Register(new CreateUsersTable());
runner.Migrate();
```

Raw-SQL migration via `SqlScriptMigration`:

```csharp
public sealed class CreateUsers : SqlScriptMigration
{
    public override long Version => 1;
    public override string Name => "CreateUsers";
    protected override string UpSql => "CREATE TABLE Users (Id TEXT PRIMARY KEY, Name TEXT);";
    protected override string? DownSql => "DROP TABLE Users;";
}
```

## `SqlSchemaBuilder` requires a connector — it used to have two paths (TASK-246, TASK-247)

**The connector is required.** `SqlSchemaBuilder`, `SqlMigrationContext` and `SqlDataMigrator` all take a
non-null `AbstractConnector`; passing null throws, and the message names where to get one. Every schema
operation delegates to the provider's own emitter (`CreateTable`, `AlterTableAdd`/`Drop`, `CreateIndexes`,
`DropIndexes`, `FieldDefinition`, `QuoteIdentifier`), so index and column DDL has **one producer per dialect**
— the same rule the connector layer follows.

It was not always so, and the history is the useful part.

`Build()` used to branch on whether a connector was supplied, with a hand-written raw-SQL fallback for the null
case. **The two branches drifted for exactly as long as nobody tested the live one.** `Build()` never copied
`_unique` onto the `IndexDefinition` it handed the connector, so a migration's `.Unique()` produced a **plain**
`CREATE INDEX` on all four providers — a missing *constraint* (TASK-246). Latent rather than firing: a sweep of
all 16 consumer repos found none declares an index through a migration. The fallback three lines below *did*
honour `_unique`, and **every** test in `Birko.Data.Migrations.SQL.Tests` built with
`new SqlSchemaBuilder(conn, null, null)` — so the feature worked in the branch nobody uses and failed in the
branch everybody uses, and a green suite said nothing. **A test that supplies `null` for a dependency may be
testing a different implementation.**

TASK-247 then deleted all eight fallbacks rather than repairing them, because two had drifted into being
**wrong on two providers**: `CREATE … INDEX IF NOT EXISTS "Col"` (MySQL rejects the clause; PostgreSQL cannot
resolve a quoted column against the folded one bare-column DDL creates) and `DROP INDEX IF EXISTS … ON …`
(MySQL rejects the `IF EXISTS` but requires the `ON`; PostgreSQL accepts the `IF EXISTS` but permits no `ON`).
A connector-free path that emits DDL two of four providers reject is not a capability. Verified
reachable-by-nobody first: the only production construction is `SqlMigrationRunner` → `SqlMigrationContext`,
which holds a non-null connector, and no consumer hand-builds a context or uses `ISchemaBuilder` at all.

Three things carried forward:

- **Those six tests now pass a real connector**, which is what makes them assertions about the shipped path.
- **`RenameField` is the one operation with no connector equivalent**, so it stays hand-written — and
  `RENAME COLUMN` is not universal (MySQL needs 8.0+, older versions need `CHANGE`), a latent per-provider gap
  of the same family, recorded because nothing calls it.
- **Composite primary keys are not supported through this builder.** The deleted fallback emitted a
  `PRIMARY KEY (a, b)` clause from `_primaryKeyFields` that `AbstractConnector.CreateTable` does not — it
  renders `PRIMARY KEY` per column from each field's flag. Nothing in the tree or any consumer declares one
  this way, so nothing in use was lost; supporting it means connector support, not a fallback.

`SqlIndexBuilder.WithField` validates its column name through `DataBase.ValidateIndexFieldIdentifier`
(TASK-249): index columns are interpolated **bare** into the statement, so caller text cannot be allowed
through unchecked.

## Dependencies
- Birko.Data.Migrations
- Birko.Data.Patterns
- Birko.Data.SQL (AbstractConnector, AbstractField, TableDefinitions)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
