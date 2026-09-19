# Birko.Data.SQL.SqLite.View

## Overview
SQLite-specific view DDL overrides for the Birko.Data.SQL.View framework. Provides `CREATE VIEW IF NOT EXISTS` syntax and `sqlite_master`-based existence checks.

## Project Location
`Birko.Data.SQL.SqLite.View/`

## Components

### Database/Connectors/SqLiteConnector_View.cs
Partial class extending `SqLiteConnector`:
- `BuildCreateViewSql(viewName, selectSql)` — Overrides base to use `CREATE VIEW IF NOT EXISTS` (SQLite does not support `CREATE OR REPLACE VIEW`)
- `ViewExists(viewName)` — Queries `sqlite_master` catalog filtering by `type = 'view'` and parameterized name

## Dependencies
- Birko.Data.SQL (AbstractConnectorBase, AbstractConnector)
- Birko.Data.SQL.View (base DDL methods: CreateView, DropView, RecreateView, etc.)
- Birko.Data.SQL.SqLite (SqLiteConnector partial class)

## Key Notes
- SQLite does not support `CREATE OR REPLACE VIEW`, so the override uses `CREATE VIEW IF NOT EXISTS` instead
- To replace an existing view in SQLite, use `RecreateView` (DROP + CREATE)
- `ViewExists` uses `sqlite_master` system table, which is SQLite's catalog for all schema objects
- Separate from base SQL.View because SQLite has fundamentally different DDL syntax (no CREATE OR REPLACE)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns, update README.md.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.
