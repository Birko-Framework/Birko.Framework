# Caching Guide

## Overview

Birko.Caching provides a unified async-first caching interface with in-memory, Redis, and hybrid (L1+L2) backends.

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

## Hybrid Cache (L1 + L2)

Two-tier cache combining a fast local (L1) memory cache with a distributed (L2) cache for multi-node consistency:

```csharp
var l1 = new MemoryCache();
var l2 = new RedisCache(redisSettings);

var options = new HybridCacheOptions
{
    L1DefaultExpiration = TimeSpan.FromSeconds(30),  // L1 entries live 30s by default
    L1MaxExpiration = TimeSpan.FromMinutes(5),       // Cap L1 TTL to limit staleness
    WriteThrough = true,                              // Write both tiers in parallel
    FallbackToL1OnL2Failure = true                    // Survive Redis outages
};

using var cache = new HybridCache(l1, l2, options);

// Set — writes both L1 and L2
await cache.SetAsync("user:42", user, CacheEntryOptions.Absolute(TimeSpan.FromMinutes(10)));

// Get — checks L1 first, falls back to L2, populates L1 on hit
var result = await cache.GetAsync<User>("user:42");

// GetOrSet — stampede-safe with two-tier lookup
var product = await cache.GetOrSetAsync("product:99",
    async ct => await db.LoadProductAsync(99, ct),
    CacheEntryOptions.Sliding(TimeSpan.FromMinutes(15)));
```

### Hybrid Cache Features

- **L1 TTL capping**: Local entries auto-expire (default 5 min max) to limit cross-node staleness
- **Write-through**: `SetAsync` writes both tiers in parallel for consistency
- **Stampede prevention**: Per-key `SemaphoreSlim` locks in `GetOrSetAsync`
- **L2 failure resilience**: Graceful fallback to L1 when distributed cache is unavailable
- **Non-owning**: `HybridCache` does NOT dispose L1/L2 — the caller owns their lifetime

### Read Flow

```
1. Check L1 (memory) → hit → return
2. Check L2 (Redis)  → hit → populate L1 → return
3. Both miss          → return Miss
```

### Configuration

| Option | Default | Description |
|--------|---------|-------------|
| `L1DefaultExpiration` | 30 seconds | Default TTL for L1 entries when no options are specified |
| `L1MaxExpiration` | 5 minutes | Maximum absolute TTL for L1 entries. Set to `null` to use original options |
| `WriteThrough` | `true` | Write both tiers in parallel (`true`) or L2-first then L1 (`false`) |
| `FallbackToL1OnL2Failure` | `true` | Silently fall back to L1 when L2 throws an exception |

## Serialization

`CacheSerializer` provides static `System.Text.Json` serialization for distributed backends. In-memory cache stores objects directly without serialization.

## See Also

- [Birko.Caching](https://github.com/birko/Birko.Caching)
- [Birko.Caching.Redis](https://github.com/birko/Birko.Caching.Redis)
- [Birko.Caching.Hybrid](https://github.com/birko/Birko.Caching.Hybrid)
- [Birko.Redis](https://github.com/birko/Birko.Redis)
