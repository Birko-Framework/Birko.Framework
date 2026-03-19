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
- `IBulkStore<T>` / `IAsyncBulkStore<T>` — Batch operations with filtering, ordering, paging
- `IStoreWrapper` — Decorator pattern for store composition
- `ITransactionalStore<T, TContext>` — External transaction participation

## Usage

This is a shared project (`.shproj`). Import it in your `.csproj`:

```xml
<Import Project="..\Birko.Data.Core\Birko.Data.Core.projitems" Label="Shared" />
<Import Project="..\Birko.Data.Stores\Birko.Data.Stores.projitems" Label="Shared" />
```

**Note:** Birko.Data.Stores requires Birko.Data.Core. Settings classes are imported transitively from Birko.Settings (namespace `Birko.Configuration`).

## License

MIT License - see [License.md](License.md)
