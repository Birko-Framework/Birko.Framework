# Birko.Data.SQL.View.Migrations.Tests

xUnit + FluentAssertions tests for [`Birko.Data.SQL.View.Migrations`](../Birko.Data.SQL.View.Migrations).

## Coverage

- **`ViewMigrationExtensionsTests`** — `CreateView` / `CreateViewAsync` / `DropView` / `DropViewAsync`
  (by-type and by-name, sync + async) produce the expected `CREATE OR REPLACE VIEW …` / `DROP VIEW IF EXISTS …`
  DDL, honor a custom quote character, and propagate the migration context's transaction onto the executed
  command; a non-`SqlMigrationContext` throws `InvalidOperationException`; null context / null view type /
  empty view name throw. Uses a recording fake `DbConnection` (SQLite rejects `CREATE OR REPLACE VIEW`, so the
  fake asserts DDL + transaction wiring independent of provider dialect).

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
