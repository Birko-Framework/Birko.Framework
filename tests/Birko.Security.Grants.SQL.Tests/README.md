# Birko.Security.Grants.SQL.Tests

Tests for `Birko.Security.Grants.SQL` on SQLite: schema, round trips, the unique token hash, the lookup indexes,
and `GrantService` running on the SQL stores.

## Test framework

xUnit 2.9 + FluentAssertions 7, Microsoft.Data.Sqlite (via Birko.Data.SQL.SqLite).

## Running tests

```
dotnet test --nologo
```

## License

See the repository root `LICENSE`.
