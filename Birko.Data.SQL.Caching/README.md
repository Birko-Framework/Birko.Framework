# Birko.Data.SQL.Caching

Query caching layer for Birko SQL stores using the decorator pattern with ICache integration.

## Features

- **CachedAsyncDataBaseBulkStore\<DB,T\>** decorator wrapping any async SQL store with transparent caching
- **SqlCacheKeyBuilder** generates SHA256 deterministic cache keys from query parameters
- **SqlCacheOptions** configures cache duration, prefix, and invalidation behavior
- **Automatic table-prefix invalidation** on write operations (Insert, Update, Delete)

## Dependencies

- Birko.Data.SQL
- Birko.Caching

## Usage

```csharp
using Birko.Data.SQL.Caching;
using Birko.Caching;

// Create the inner store as usual
var innerStore = new MyAsyncDataBaseBulkStore<MyDb, MyModel>();

// Wrap with caching
var cache = new MemoryCache();
var options = new SqlCacheOptions
{
    DefaultExpiration = TimeSpan.FromMinutes(10),
    TablePrefix = "mymodel"
};

var cachedStore = new CachedAsyncDataBaseBulkStore<MyDb, MyModel>(innerStore, cache, options);

// Reads are served from cache when available
var item = await cachedStore.ReadAsync(id);

// Writes automatically invalidate related cache entries
await cachedStore.InsertAsync(newItem);
```

## API Reference

- **CachedAsyncDataBaseBulkStore\<DB,T\>** - Decorator that intercepts read/write operations with cache logic
- **SqlCacheKeyBuilder** - Builds deterministic SHA256 keys from table name, query, and parameters
- **SqlCacheOptions** - `DefaultExpiration`, `TablePrefix`, `InvalidateOnWrite`

## Related Projects

- [Birko.Data.SQL](../Birko.Data.SQL/) - SQL base classes
- [Birko.Caching](../Birko.Caching/) - Caching framework

## License

Part of the Birko Framework.
