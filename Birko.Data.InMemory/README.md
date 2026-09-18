# Birko.Data.InMemory

In-memory store backend for the Birko data layer. Entities are held in a thread-safe
`ConcurrentDictionary<Guid, T>` for the lifetime of the store instance — nothing is persisted.

Use it for:

- **Testing** — a single, correct test double implementing the full store contract, instead of
  hand-rolling a fake per test project.
- **Reference / prototyping** — the simplest possible store; spin up a Birko app with no DB or
  files.

## Stores

| Type | Base | Description |
|------|------|-------------|
| `InMemoryStore<T>` | `AbstractBulkStore<T>` | Synchronous bulk store |
| `AsyncInMemoryStore<T>` | `AbstractAsyncBulkStore<T>` | Asynchronous bulk store |

Both implement `IStore<T>`, `IBulkStore<T>` (sync) / `IAsyncStore<T>`, `IAsyncBulkStore<T>`
(async), the aggregation interfaces, and `ISettingsStore<Settings>`.

## Example

```csharp
using Birko.Data.InMemory.Stores;

var store = new AsyncInMemoryStore<Product>();

var id = await store.CreateAsync(new Product { Name = "Widget" });
var loaded = await store.ReadAsync(id);

await store.CreateAsync(new[]
{
    new Product { Name = "A" },
    new Product { Name = "B" },
});

var matches = await store.ReadAsync(p => p.Name.StartsWith("A"));
var total   = await store.CountAsync();

// Filter-based bulk delete
await store.DeleteAsync(p => p.Name == "B");
```

## Settings

In-memory stores have no connection string. `Settings` (Location / Name) are accepted only so
the store is drop-in compatible with file/SQL stores in shared wiring; they are otherwise unused.

## Notes

- Concrete stores override the `*Core` methods, never the public CRUD methods (lazy-init is
  preserved by the base class).
- Bulk reads return a `List<T>` snapshot, so the backing dictionary can mutate concurrently
  without breaking enumeration.
- All data is discarded on `Destroy()` / `DestroyAsync()`.

## License

MIT — see [License.md](License.md).
