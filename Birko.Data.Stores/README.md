# Birko.Data.Stores

Store abstractions for the Birko Framework. Contains store interfaces, abstract implementations, ordering, and service locator. Settings hierarchy (Settings, PasswordSettings, RemoteSettings) is provided by `Birko.Settings` and imported transitively.

## Store Hierarchy

```
AbstractStore<T>
    -> AbstractBulkStore<T> (sync, with ordering/paging)

AbstractAsyncStore<T>
    -> AbstractAsyncBulkStore<T> (async, with ordering/paging)
```

## Settings Chain

```
Settings (Location, Name)
    -> PasswordSettings (Password)
        -> RemoteSettings (UserName, Port, UseSecure)
```

## Key Interfaces

- `IStore<T>` / `IAsyncStore<T>` — Single-entity CRUD operations
- `IBulkStore<T>` / `IAsyncBulkStore<T>` — Batch operations with filtering, ordering, paging, filter-based update/delete
- `PropertyUpdate<T>` — Fluent builder for native partial property updates (SQL SET, MongoDB $set, ES scripts)
- `IStoreWrapper` — Decorator pattern for store composition
- `ITransactionalStore<T, TContext>` — External transaction participation

## Filter-Based Bulk Operations

Bulk stores support three patterns for update/delete by filter:

```csharp
// 1. PropertyUpdate — native platform operation (single SQL UPDATE SET WHERE)
store.Update(
    x => x.Category == "old",
    new PropertyUpdate<Product>().Set(x => x.Active, false).Set(x => x.Category, "archived")
);

// 2. Action<T> — read-modify-save (for complex mutations)
store.Update(x => x.Price > 100, item => { item.Price *= 0.9m; });

// 3. Delete by filter — native platform operation (single SQL DELETE WHERE)
store.Delete(x => x.IsExpired);
```

| Platform | PropertyUpdate | Delete(filter) |
|----------|---------------|----------------|
| SQL | Native `UPDATE SET WHERE` | Native `DELETE WHERE` |
| MongoDB | `UpdateMany` with `$set` | `DeleteMany` |
| ElasticSearch | `UpdateByQuery` (Painless) | `DeleteByQuery` |
| Others | Fallback read-modify-save | Fallback read-then-delete |

## Usage

This is a shared project (`.shproj`). Import it in your `.csproj`:

```xml
<Import Project="..\Birko.Data.Core\Birko.Data.Core.projitems" Label="Shared" />
<Import Project="..\Birko.Data.Stores\Birko.Data.Stores.projitems" Label="Shared" />
```

**Note:** Birko.Data.Stores requires Birko.Data.Core. Settings classes are imported transitively from Birko.Settings (namespace `Birko.Configuration`).

## License

MIT License - see [License.md](License.md)
