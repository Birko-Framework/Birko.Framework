# Birko.Data.Repositories

Repository abstractions for the Birko Framework. Contains repository interfaces, abstract implementations, service locator, and DI extensions.

## Repository Hierarchy

```
AbstractRepository<T> (wraps IStore<T>)
    -> AbstractBulkRepository<T> (wraps IBulkStore<T>)

AbstractAsyncRepository<T> (wraps IAsyncStore<T>)
    -> AbstractAsyncBulkRepository<T> (wraps IAsyncBulkStore<T>)
```

## Key Interfaces

- `IRepository<T>` / `IAsyncRepository<T>` — Single-entity CRUD via repositories
- `IBulkRepository<T>` / `IAsyncBulkRepository<T>` — Batch operations with filtering, ordering, paging, filter-based update/delete
- `RepositoryLocator` — Thread-safe factory/cache for repository instances
- `ServiceCollectionExtensions` — DI registration (AddRepository, AddRepositorySingleton, AddRepositoryScoped)

## Usage

This is a shared project (`.shproj`). Import it in your `.csproj`:

```xml
<Import Project="..\Birko.Data.Core\Birko.Data.Core.projitems" Label="Shared" />
<Import Project="..\Birko.Data.Stores\Birko.Data.Stores.projitems" Label="Shared" />
<Import Project="..\Birko.Data.Repositories\Birko.Data.Repositories.projitems" Label="Shared" />
```

**Note:** Birko.Data.Repositories requires both Birko.Data.Core and Birko.Data.Stores.

## License

MIT License - see [License.md](License.md)
