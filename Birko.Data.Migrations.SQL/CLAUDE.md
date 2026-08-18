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

## `SqlSchemaBuilder` has two paths, and they are not equivalent (TASK-246)

`CreateCollection(...)` / `CreateIndex(...)` return fluent builders whose `Build()` branches on whether a
**connector** was supplied:

| branch | taken when | notes |
|---|---|---|
| connector path | `connector != null` — **every production migration** | builds a `Tables.IndexDefinition` and calls `connector.CreateIndexes(...)`, so it inherits the provider's emitter |
| raw-SQL fallback | `connector == null` | hand-written statement on the supplied connection |

**Test the connector path.** The two branches drifted for exactly as long as nobody did: `Build()` never
copied `_unique` onto the `IndexDefinition`, so a migration's `.Unique()` produced a **plain**
`CREATE INDEX` on all four providers — a missing *constraint*, silently accepting the duplicate rows the
migration was written to forbid. The fallback three lines below *did* honour `_unique`, and every test in
`Birko.Data.Migrations.SQL.Tests` built with `new SqlSchemaBuilder(conn, null, null)` — so the feature was
demonstrably working in the branch nobody uses and broken in the branch everybody uses, and the suite could
not tell. A test that supplies `null` for the connector is testing the fallback, whatever it looks like it is
testing.

Two consequences worth keeping:

- **`Unique` is not the only thing set in that object initialiser** — column order and `IsDescending` are
  populated in the same expression and had no test either, so they are pinned now alongside it.
- **The fallback is still known-broken on two providers** and is out of scope of that fix: it emits
  `CREATE … INDEX IF NOT EXISTS` with quoted columns (rejected by MySQL, unresolvable against PostgreSQL's
  folded columns) and `DROP INDEX IF EXISTS … ON …`, which is wrong on MySQL and PostgreSQL in opposite
  directions. Tracked as TASK-247, whose first question is whether the fallback should exist at all now that
  the connector emitters are correct on every provider.

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
