# Birko.Data.SQL.SqLite.Tests

xUnit + FluentAssertions tests for [`Birko.Data.SQL.SqLite`](../Birko.Data.SQL.SqLite).

## Coverage

- **`SqLiteSqlStoreFactoryTests`** — `SqLiteSqlStoreFactory` built from `SqLiteSettings` creates a missing database directory eagerly, and its connector and async stores reach the configured on-disk database (the table is created and queried back via `sqlite_master`). The older `SqLiteStoreFactory` and `AddSqLiteStores` were removed at TASK-1001.

## Test framework

- xUnit
- FluentAssertions
- Microsoft.Data.Sqlite
- Microsoft.Extensions.DependencyInjection

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
