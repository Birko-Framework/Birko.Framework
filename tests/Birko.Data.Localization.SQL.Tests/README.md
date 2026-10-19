# Birko.Data.Localization.SQL.Tests

Tests for [Birko.Data.Localization.SQL](../../Birko.Data.Localization.SQL/README.md): the translation table's unique key
on SQLite and, with a server, PostgreSQL, SQL Server and MySQL.

## Test framework

xUnit + FluentAssertions.

## Running tests

```sh
dotnet test --nologo
BIRKO_PG_HOST=127.0.0.1 BIRKO_MSSQL_HOST=127.0.0.1 BIRKO_MYSQL_HOST=127.0.0.1 BIRKO_REQUIRE_LIVE=1 dotnet test --nologo
```

## License

MIT — see the repository's [LICENSE](../../LICENSE).
