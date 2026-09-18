# Birko.Data.Sync.Xml.Tests

xUnit + FluentAssertions tests for [`Birko.Data.Sync.Xml`](../Birko.Data.Sync.Xml) (CR-M163) — the XML file-based sync knowledge store.

## Coverage

- **`AsyncJsonSyncKnowledgeStoreTests`** — SetLastSyncTime updates every matching item in one bulk write (CR-M171) and GetLastSyncTime returns the max; scope isolation; null/empty cases; CreateKnowledgeItem deletion-flag derivation. Runs against a real temp-file XML store.

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
