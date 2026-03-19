# Birko.Data.SQL.MySQL.View

## Overview
MySQL-specific view DDL overrides for the Birko.Data.SQL.View framework. Provides `information_schema.VIEWS`-based existence checks scoped to the current database.

## Project Location
`C:\Source\Birko.Data.SQL.MySQL.View\`

## Components

### Database/Connectors/MySQLConnector_View.cs
Partial class extending `MySQLConnector`:
- `ViewExists(viewName)` — Queries `information_schema.VIEWS` with `TABLE_SCHEMA = DATABASE()` to scope to the current database

Note: `BuildCreateViewSql` is NOT overridden because MySQL natively supports `CREATE OR REPLACE VIEW` (the base default).

## Dependencies
- Birko.Data.SQL (AbstractConnectorBase, AbstractConnector)
- Birko.Data.SQL.View (base DDL methods: CreateView, DropView, RecreateView, etc.)
- Birko.Data.SQL.MySQL (MySQLConnector partial class)

## Key Notes
- MySQL uses the base `CREATE OR REPLACE VIEW` syntax (no override needed)
- `ViewExists` uses `information_schema.VIEWS` with `TABLE_SCHEMA = DATABASE()` filtering to avoid false positives from views in other databases on the same server
- Separate from base SQL.View because each SQL provider has different catalog queries and schema scoping

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns, update README.md.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.
