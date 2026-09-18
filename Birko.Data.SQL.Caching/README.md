# Birko.Data.SQL.Caching

Transparent query caching for Birko async SQL bulk stores, via an `ICache`-backed subclass of `AsyncDataBaseBulkStore<DB,T>`.

## Features

- **CachedAsyncDataBaseBulkStore\<DB,T\>** — a subclass of `AsyncDataBaseBulkStore<DB,T>` that caches read results and invalidates the table's cache entries on writes. It is a store in its own right (not a wrapper around a separate inner store).
- **SqlCacheKeyBuilder** — builds deterministic keys `sql:{table}:{filterHash}:{orderHash}:{limit}:{offset}` (SHA-256 of the filter/order strings, truncated to 16 hex chars; `_` sentinel for absent parts).
- **SqlCacheOptions** — configures cache duration and an on/off switch.
- **Automatic table-prefix invalidation** on every write (`Create`/`Update`/`Delete`, including the filter-based overloads).

## Dependencies

- Birko.Data.SQL
- Birko.Caching

## Usage

`CachedAsyncDataBaseBulkStore<DB,T>` is a normal Birko store: construct it with an `ICache` (and optional `SqlCacheOptions`), then configure it with `SetSettings` exactly like the base `AsyncDataBaseBulkStore<DB,T>`. Reads/writes go through the standard `IAsyncBulkStore<T>` surface — there is no separate `ReadAsync(id)`/`InsertAsync` API.

```csharp
using Birko.Data.SQL.Stores;   // CachedAsyncDataBaseBulkStore lives here
using Birko.Data.SQL.Caching;  // SqlCacheOptions / SqlCacheKeyBuilder
using Birko.Caching;

var cache = new MemoryCache();
var options = new SqlCacheOptions
{
    DefaultExpiration = TimeSpan.FromMinutes(10),
    Enabled = true,
};

var store = new CachedAsyncDataBaseBulkStore<MyDb, MyModel>(cache, options);
store.SetSettings(mySqlSettings);   // inherited from AsyncDataBaseBulkStore

// Reads are served from cache when available, then populated on miss.
var item = await store.ReadFirstAsync(x => x.Guid == id);
var page = await store.ReadAsync(filter: null, orderBy: null, limit: 20, offset: 0);

// Writes go through the normal bulk surface and invalidate the table's cache entries.
await store.CreateAsync(new[] { newItem });
```

When `Enabled` is `false`, every operation delegates straight to the base store (no caching, no invalidation).

## API Reference

- **CachedAsyncDataBaseBulkStore\<DB,T\>** — subclass of `AsyncDataBaseBulkStore<DB,T>`; constructor `(ICache cache, SqlCacheOptions? options = null)`. Overrides the `*CoreAsync` read/write methods to add caching + invalidation; inherits `SetSettings`, `InitAsync`, etc. from the base.
- **SqlCacheKeyBuilder** — `BuildKey(tableName, filterString, orderString, limit, offset)` and `GetTablePrefix(tableName)` (the prefix used for bulk table invalidation).
- **SqlCacheOptions** — `DefaultExpiration` (default 5 min) and `Enabled` (default true).

## Related Projects

- [Birko.Data.SQL](../Birko.Data.SQL/) - SQL base classes
- [Birko.Caching](../Birko.Caching/) - Caching framework

## License

Part of the Birko Framework.
