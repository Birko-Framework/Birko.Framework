# Birko.Data.SQL.MSSql.View

## Overview
SQL Server-specific view DDL overrides for the Birko.Data.SQL.View framework. Provides `CREATE OR ALTER VIEW` syntax and `sys.views` catalog-based existence checks.

## Project Location
`C:\Source\Birko.Data.SQL.MSSql.View\`

## Components

### Database/Connector/MSSqlConnector_View.cs
Partial class extending `MSSqlConnector`. Regular (non-indexed) views:
- `BuildCreateViewSql(viewName, selectSql)` — Overrides base to use `CREATE OR ALTER VIEW` (SQL Server 2016 SP1+)
- `ViewExists(viewName)` — Queries `sys.views` catalog with parameterized name lookup

Indexed (materialized) views — created `WITH SCHEMABINDING` + a unique clustered index that the engine
maintains automatically:
- `CreateIndexedView(viewType, viewName?)` / `CreateIndexedViewAsync(...)` — creates the SCHEMABINDING view
  (two-part `[schema].[table]` names) then a `UNIQUE CLUSTERED INDEX` over the key columns.
- `DropIndexedView(viewName)` / `DropIndexedViewAsync(...)` — drops the clustered index + view.
- `IndexedViewExists(viewName)` / `IndexedViewExistsAsync(...)` — checks whether a clustered index exists on the view.
- `GetIndexedViewKeyColumns(view)` (internal) — resolves the clustered-index key columns from the view's
  primary/unique fields (falls back to the first field).
- `BuildSchemaBindingSelectSql(view)` (internal) — the two-part-name SELECT, delegating to the shared
  `ViewSelectSqlBuilder` with a `[schema].[table]` qualifier.
- `GetSchemaName()` (`protected virtual`, default `"dbo"`) — override for non-dbo schemas.

**Requirements / limitations:** indexed views require SQL Server SCHEMABINDING support and two-part names.
**Aggregate (GROUP BY) indexed views are rejected** by `CreateIndexedView` with a clear `NotSupportedException`
— SQL Server requires `COUNT_BIG(*)` in the select list, which the generic aggregate SELECT builder does not
emit (create those manually).

## Dependencies
- Birko.Data.SQL (AbstractConnectorBase, AbstractConnector)
- Birko.Data.SQL.View (base DDL methods: CreateView, DropView, RecreateView, etc.)
- Birko.Data.SQL.MSSql (MSSqlConnector partial class)

## Key Notes
- Uses `CREATE OR ALTER VIEW` instead of `CREATE OR REPLACE VIEW` (SQL Server syntax)
- Requires SQL Server 2016 SP1 or later for `CREATE OR ALTER` support
- `ViewExists` uses `sys.views` system catalog rather than `information_schema` for better performance
- Separate from base SQL.View because each SQL provider has different DDL syntax and catalog queries

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns, update README.md.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.
