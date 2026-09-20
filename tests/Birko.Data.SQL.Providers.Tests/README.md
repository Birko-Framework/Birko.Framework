# Birko.Data.SQL.Providers.Tests

xUnit + FluentAssertions tests for the SQL provider family —
[`Birko.Data.SQL.MSSql`](../Birko.Data.SQL.MSSql),
[`Birko.Data.SQL.MySQL`](../Birko.Data.SQL.MySQL), and
[`Birko.Data.SQL.PostgreSQL`](../Birko.Data.SQL.PostgreSQL) — covering the cross-provider
store-factory + DI backport (EPIC-016 / TASK-042).

## Coverage

- **`ProviderStoreFactoryTests`** — one class, `[Fact]`s grouped per provider over a nested
  `Widget : AbstractModel` fixture:
  - MSSql: `MSSqlStoreFactory` builds settings + connection string (Initial Catalog, MARS) and yields
    an async store + connector; `MSSqlStore<T>.SetSettings` keeps the full `MSSqlSettings`
    (regression for TASK-051); `AddMSSqlStores` registers a resolvable `IMSSqlStoreFactory` singleton.
  - MySQL: `MySQLStoreFactory` builds settings, carries `BulkInsertBatchSize`, yields a store +
    connector; `AddMySqlStores` registers a resolvable `IMySQLStoreFactory` singleton.
  - PostgreSQL: `PostgreSQLStoreFactory` builds settings, carries `UseBinaryImport`, yields a store +
    connector; `AddPostgreSqlStores` registers a resolvable `IPostgreSQLStoreFactory` singleton.
  - Live CRUD round-trips are opt-in per provider via `BIRKO_MSSQL_HOST` / `BIRKO_MYSQL_HOST` /
    `BIRKO_PG_HOST` (with optional `_PORT` / `_USER` / `_PASSWORD` / `_DB`), and skipped when absent
    — the same gate the rest of the SQL suites use. `BIRKO_REQUIRE_LIVE` makes an absent server a
    failure instead of a skip.

## Test framework

- xUnit
- FluentAssertions
- Microsoft.Extensions.DependencyInjection (for the `AddXStores` DI checks)
- Npgsql (PostgreSQL driver; MSSql + MySQL drivers come from the imported `.projitems`)

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
