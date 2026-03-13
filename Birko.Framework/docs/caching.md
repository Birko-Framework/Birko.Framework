# Caching Guide

## Overview

Birko.Caching provides a unified async-first caching interface with in-memory and Redis backends.

## Core Interface

### ICache

```csharp
public interface ICache : IDisposable
{
    Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
                              CacheEntryOptions? options = null, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
```

### CacheResult<T>

Distinguishes "not found" from "found null value":

```csharp
var result = await cache.GetAsync<Product>("product:123");
if (result.HasValue)
{
    var product = result.Value;  // May be null if null was cached
}
```

### CacheEntryOptions

```csharp
// Absolute expiration (removed after fixed duration)
var opts = CacheEntryOptions.Absolute(TimeSpan.FromMinutes(10));

// Sliding expiration (renewed on each access)
var opts = CacheEntryOptions.Sliding(TimeSpan.FromMinutes(5));

// Priority (NeverRemove survives eviction)
var opts = new CacheEntryOptions
{
    AbsoluteExpiration = TimeSpan.FromHours(1),
    Priority = CachePriority.NeverRemove
};
```

## In-Memory Cache

ConcurrentDictionary-based with background expiration timer:

```csharp
using var cache = new MemoryCache();

await cache.SetAsync("key", myObject, CacheEntryOptions.Absolute(TimeSpan.FromMinutes(10)));

// Stampede-safe factory pattern:
var product = await cache.GetOrSetAsync("product:123",
    async ct => await db.GetProductAsync(123, ct),
    CacheEntryOptions.Sliding(TimeSpan.FromMinutes(5)));

// Invalidate by prefix:
await cache.RemoveByPrefixAsync("product:");
```

`GetOrSetAsync` uses per-key `SemaphoreSlim` to prevent cache stampede (multiple concurrent requests for the same missing key).

## Redis Cache

Distributed Redis backend via StackExchange.Redis. Uses `RedisSettings` from `Birko.Redis` (extends the framework's `RemoteSettings` hierarchy):

```csharp
var settings = new RedisSettings
{
    Location = "localhost",
    Port = 6379,
    KeyPrefix = "myapp",             // Keys prefixed as "myapp:{key}"
    Database = 0
};

using var cache = new RedisCache(settings, defaultExpiration: TimeSpan.FromMinutes(10));

await cache.SetAsync("user:42", user, CacheEntryOptions.Sliding(TimeSpan.FromMinutes(15)));
var result = await cache.GetAsync<User>("user:42");
```

### Redis Features

- **Stampede prevention**: Uses Redis `SET NX` for distributed locking
- **Sliding expiration**: Hash metadata tracks last access time
- **Prefix removal**: Uses `SCAN` for efficient prefix-based invalidation
- **ClearAsync**: Flushes database or prefix depending on `InstanceName`

## Serialization

`CacheSerializer` provides static `System.Text.Json` serialization for distributed backends. In-memory cache stores objects directly without serialization.

## See Also

- [Birko.Caching CLAUDE.md](../Birko.Caching/CLAUDE.md)
- [Birko.Caching.Redis CLAUDE.md](../Birko.Caching.Redis/CLAUDE.md)
- [Birko.Redis CLAUDE.md](../Birko.Redis/CLAUDE.md)
