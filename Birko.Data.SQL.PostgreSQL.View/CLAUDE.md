# Birko.Data.SQL.PostgreSQL.View

## Overview
PostgreSQL-specific view DDL overrides for the Birko.Data.SQL.View framework. Provides `information_schema`-based existence checks and materialized view support.

## Project Location
`C:\Source\Birko.Data.SQL.PostgreSQL.View\`

## Components

### Database/Connectors/PostgreSQLConnector_View.cs
Partial class extending `PostgreSQLConnector`:
- `ViewExists(viewName)` — Queries `information_schema.views` with parameterized name lookup
- `CreateMaterializedView(viewType, viewName?)` — Creates a materialized view using `CREATE MATERIALIZED VIEW IF NOT EXISTS`
- `RefreshMaterializedView(viewName, concurrently?)` — Refreshes a materialized view, optionally with `CONCURRENTLY` (requires unique index)
- `DropMaterializedView(viewName)` — Drops a materialized view using `DROP MATERIALIZED VIEW IF EXISTS`

Note: `BuildCreateViewSql` is NOT overridden because PostgreSQL natively supports `CREATE OR REPLACE VIEW` (the base default).

## Dependencies
- Birko.Data.SQL (AbstractConnectorBase, AbstractConnector)
- Birko.Data.SQL.View (base DDL methods: CreateView, DropView, RecreateView, etc.)
- Birko.Data.SQL.PostgreSQL (PostgreSQLConnector partial class)

## Key Notes
- PostgreSQL uses the base `CREATE OR REPLACE VIEW` syntax (no override needed)
- `ViewExists` uses `information_schema.views` for standard SQL compliance
- Materialized views are a PostgreSQL-specific feature not available in other providers
- `REFRESH MATERIALIZED VIEW CONCURRENTLY` requires a unique index on the materialized view
- Separate from base SQL.View because each SQL provider has different catalog queries and provider-specific features (materialized views)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns, update README.md.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.
