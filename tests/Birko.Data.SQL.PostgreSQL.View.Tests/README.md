# Birko.Data.SQL.PostgreSQL.View.Tests

xUnit + FluentAssertions tests for `Birko.Data.SQL.PostgreSQL.View` (the materialized-view surface of
`PostgreSQLConnector`). Offline — asserts the DDL composition (`CREATE MATERIALIZED VIEW IF NOT EXISTS`,
`REFRESH ... [CONCURRENTLY]`, `DROP MATERIALIZED VIEW IF EXISTS`) via the internal `Build*` helpers plus
the argument-validation guards. The create/refresh/drop round-trip needs a live PostgreSQL (integration tier).
