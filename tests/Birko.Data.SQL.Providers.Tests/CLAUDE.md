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
  (no server). The live CRUD round-trips (`MSSql_live_crud_round_trip` and the `RunLiveCrud` helper)
  are opt-in via env vars (e.g. `BIRKO_MSSQL_TEST` = `"host;db;user;pass"`) and are skipped when the
  env var is absent.

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`, nullable + implicit usings, `IsTestProject`).
  Imports the SQL stack `.projitems` (`Birko.Data.SQL`, `Birko.Data.SQL.MSSql`,
  `Birko.Data.SQL.MySQL`, `Birko.Data.SQL.PostgreSQL`, plus their `Birko.Data.*` / `Birko.Models.*`
  dependencies). MSSql + MySQL drivers flow in from those `.projitems`; `Npgsql` is added here as a
  package alongside `Microsoft.Extensions.DependencyInjection`.
- One test class per source type; test both success and failure/guard paths.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
