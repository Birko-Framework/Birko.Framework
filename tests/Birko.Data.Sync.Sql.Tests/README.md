# Birko.Data.Sync.Sql.Tests

xUnit + FluentAssertions tests for [`Birko.Data.Sync.Sql`](../Birko.Data.Sync.Sql) (CR-M166), against a real on-disk SQLite connector.

## Coverage

- **`AsyncSqlSyncKnowledgeStoreTests`** — GetLastSyncTime Max/null-on-empty; SetLastSyncTime updates all matching items + scope isolation + null; CreateKnowledgeItem deletion-flag derivation.

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
