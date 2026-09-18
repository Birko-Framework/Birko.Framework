# Birko.Data.Sync.Json.Tests

xUnit + FluentAssertions tests for [`Birko.Data.Sync.Json`](../Birko.Data.Sync.Json) (CR-M163) — the JSON file-based sync knowledge store.

## Coverage

- **`AsyncJsonSyncKnowledgeStoreTests`** — SetLastSyncTime updates every matching item in one bulk write (CR-M162) and GetLastSyncTime returns the max; scope isolation; null/empty cases; CreateKnowledgeItem deletion-flag derivation. Runs against a real temp-file JSON store.

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
