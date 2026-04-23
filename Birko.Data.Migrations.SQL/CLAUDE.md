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
- `SqlMigrationSettings` — UseTransaction, MigrationsTable, SchemaName

### Internal
- `SchemaField` — Extends AbstractField with null PropertyInfo (DDL-only). Maps FieldType to DbType.

## Usage

```csharp
var runner = new SqlMigrationRunner(store.Connector);
runner.Register(new CreateUsersTable());
runner.Migrate();
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
