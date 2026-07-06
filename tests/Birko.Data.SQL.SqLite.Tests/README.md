# Birko.Data.SQL.SqLite.Tests

xUnit + FluentAssertions tests for [`Birko.Data.SQL.SqLite`](../Birko.Data.SQL.SqLite).

## Coverage

- **`SqLiteStoreFactoryTests`** — `SqLiteStoreFactory` creates a missing database directory eagerly; resolves a relative `Location` against `BaseDirectory` and ignores it for a rooted `Location`; `GetConnector` is wired to the configured on-disk database (creates the table, queried back via `sqlite_master`) and `GetStore<T>` returns a store bound to the same settings; the `AddSqLiteStores` DI extension registers a resolvable singleton `ISqLiteStoreFactory`.

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
