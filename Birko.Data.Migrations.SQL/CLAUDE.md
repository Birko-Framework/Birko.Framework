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

## Dependencies
- Birko.Data.Migrations
- Birko.Data.Patterns
- Birko.Data.SQL (AbstractConnector, AbstractField, TableDefinitions)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
