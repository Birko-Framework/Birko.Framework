# Birko.Data.SQL.PostgreSQL.View.Tests

Test project for `Birko.Data.SQL.PostgreSQL.View`.

## Scope
- Materialized-view DDL composition via the internal `BuildCreateMaterializedViewSql` /
  `BuildRefreshMaterializedViewSql` / `BuildDropMaterializedViewSql` helpers (identifier quoting,
  `IF NOT EXISTS` / `CONCURRENTLY` / `IF EXISTS`).
- Argument-validation guards: null/empty viewName → `ArgumentException` (sync + the async variants,
  which validate before `Task.Run`); a non-view type → `InvalidOperationException`.

## Conventions
- xUnit + FluentAssertions; offline (connectors are lazy). The actual DDL execution / round-trip is an
  integration-tier concern requiring a live PostgreSQL.
