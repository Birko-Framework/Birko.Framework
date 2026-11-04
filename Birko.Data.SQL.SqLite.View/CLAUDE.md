# Birko.Data.SQL.SqLite.View

## Overview
SQLite-specific view DDL overrides for the Birko.Data.SQL.View framework. Provides a drop-then-create view DDL (SQLite has no `CREATE OR REPLACE VIEW`) and `sqlite_master`-based existence checks.

## Project Location
`Birko.Data.SQL.SqLite.View/`

## Components

### Database/Connectors/SqLiteConnector_View.cs
Partial class extending `SqLiteConnector`:
- `BuildCreateViewSql(viewName, selectSql)` — `DROP VIEW IF EXISTS …; CREATE VIEW … AS …`, run in `CreateView`'s own transaction, so an existing definition is replaced (TASK-717; it was `CREATE VIEW IF NOT EXISTS`, which kept the old one silently)
- `ViewExists(viewName)` — Queries `sqlite_master` catalog filtering by `type = 'view'` and parameterized name

## Dependencies
- Birko.Data.SQL (AbstractConnectorBase, AbstractConnector)
- Birko.Data.SQL.View (base DDL methods: CreateView, DropView, RecreateView, etc.)
- Birko.Data.SQL.SqLite (SqLiteConnector partial class)

## Key Notes
- SQLite does not support `CREATE OR REPLACE VIEW`, so the override drops and creates in one transaction
- To replace an existing view in SQLite, use `RecreateView` (DROP + CREATE)
- `ViewExists` uses `sqlite_master` system table, which is SQLite's catalog for all schema objects
- Separate from base SQL.View because SQLite has fundamentally different DDL syntax (no CREATE OR REPLACE)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns, update README.md.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.
