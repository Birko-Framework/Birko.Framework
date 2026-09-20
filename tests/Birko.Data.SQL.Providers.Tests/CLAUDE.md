# Birko.Data.SQL.Providers.Tests

## Overview

xUnit + FluentAssertions test project for the cross-provider SQL store-factory + DI backport
(EPIC-016 / TASK-042): the `Add{MSSql,MySql,PostgreSql}Stores` extensions and the
`{P}StoreFactory` / `{P}StoreFactoryOptions` / settings types across MSSql, MySQL, and PostgreSQL.

## Project Location

`tests/Birko.Data.SQL.Providers.Tests/`

## Scope

- `ProviderStoreFactoryTests` — the single test class, with `[Fact]`s grouped per provider over a
  nested `Widget : AbstractModel` fixture:
  - **MSSql** — `MSSqlStoreFactory` builds `MSSqlSettings` and a connection string (Initial Catalog,
    MARS) and yields an async store + connector; `MSSqlStore<T>.SetSettings` retains the full
    `MSSqlSettings` (UserName / MARS survive, regression for TASK-051, not a narrowed
    `PasswordSettings`); `AddMSSqlStores` registers a resolvable `IMSSqlStoreFactory` singleton.
  - **MySQL** — `MySQLStoreFactory` builds settings, carries `BulkInsertBatchSize`, and yields an
    async store + connector; `AddMySqlStores` registers a resolvable `IMySQLStoreFactory` singleton.
  - **PostgreSQL** — `PostgreSQLStoreFactory` builds settings, carries `UseBinaryImport`, and yields
    an async store + connector; `AddPostgreSqlStores` registers a resolvable
    `IPostgreSQLStoreFactory` singleton.
- The above construction / settings / connection-string / DI-registration checks all run offline
  (no server). The three live CRUD round-trips (`{MSSql,MySql,PostgreSql}_live_crud_round_trip`, via
  the `Resolve` helper) are opt-in per provider on the **same env-var group every other SQL suite in
  the tree reads** — `BIRKO_MSSQL_HOST`, `BIRKO_MYSQL_HOST`, `BIRKO_PG_HOST`, each with optional
  `_PORT` / `_USER` / `_PASSWORD` / `_DB` companions defaulting to the `live-tests.yml` containers.
  `_HOST` alone opts a run in; absent, the test writes a skip line and returns. Setting
  `BIRKO_REQUIRE_LIVE` turns that skip into a failure, which is how CI refuses to go green on a
  fixture that never came up.
- ⚠ **Do not invent a gate of this suite's own.** It originally read a
  `BIRKO_{PROVIDER}_TEST=host;db;user;pass` variable nobody else used, so CI — which sets the group
  above — never opted the round-trips in, and `BIRKO_REQUIRE_LIVE` turned all three red on every
  live-tests run. A second convention for one suite is not a smaller change than reading the first.

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`, nullable + implicit usings, `IsTestProject`).
  Imports the SQL stack `.projitems` (`Birko.Data.SQL`, `Birko.Data.SQL.MSSql`,
  `Birko.Data.SQL.MySQL`, `Birko.Data.SQL.PostgreSQL`, plus their `Birko.Data.*` / `Birko.Models.*`
  dependencies). MSSql + MySQL drivers flow in from those `.projitems`; `Npgsql` is added here as a
  package alongside `Microsoft.Extensions.DependencyInjection`.
- One test class per source type; test both success and failure/guard paths.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
