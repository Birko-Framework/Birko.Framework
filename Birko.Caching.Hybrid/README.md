# Birko.Caching.Hybrid

Two-tier hybrid cache for the Birko Framework — combines a fast local (L1) cache with a distributed (L2) cache for multi-node consistency.

## Features

- **L1 + L2 two-tier caching** — sub-microsecond local reads with distributed fallback
- **Write-through** — SetAsync writes both tiers in parallel for consistency
- **L1 TTL capping** — local entries auto-expire to limit staleness across nodes
- **Stampede prevention** — per-key locking in GetOrSetAsync prevents redundant factory calls
- **L2 failure resilience** — graceful fallback to L1 when the distributed cache is unavailable
- **Cache-agnostic** — works with any ICache implementations (MemoryCache, RedisCache, or custom)

## Dependencies

- **Birko.Caching** — ICache interface, CacheEntryOptions, CacheResult

## Usage

```csharp
using Birko.Caching.Hybrid;
using Birko.Caching.Memory;
using Birko.Caching.Redis;

// Create L1 (local) and L2 (distributed) caches
var l1 = new MemoryCache();
var l2 = new RedisCache(redisSettings);

// Configure hybrid behavior
var options = new HybridCacheOptions
{
    L1DefaultExpiration = TimeSpan.FromSeconds(30),  // L1 entries live 30s by default
    L1MaxExpiration = TimeSpan.FromMinutes(5),       // L1 never exceeds 5 min
    WriteThrough = true,                              // Write both tiers in parallel
    FallbackToL1OnL2Failure = true                    // Survive Redis outages
};

using var cache = new HybridCache(l1, l2, options);

// Set — writes both L1 and L2
await cache.SetAsync("user:42", user, CacheEntryOptions.Absolute(TimeSpan.FromMinutes(10)));

// Get — checks L1, falls back to L2, populates L1 on hit
var result = await cache.GetAsync<User>("user:42");
if (result.HasValue)
    Console.WriteLine(result.Value!.Name);

// GetOrSet — stampede-safe factory with two-tier lookup
var product = await cache.GetOrSetAsync("product:99",
    async ct => await db.LoadProductAsync(99, ct),
    CacheEntryOptions.Sliding(TimeSpan.FromMinutes(15)));

// Remove — evicts from both tiers
await cache.RemoveAsync("user:42");
```

## Configuration

| Option | Default | Description |
|--------|---------|-------------|
| `L1DefaultExpiration` | 30 seconds | Default TTL for L1 entries when no options are specified |
| `L1MaxExpiration` | 5 minutes | Maximum absolute TTL for L1 entries (caps requested TTL). Set to `null` to use original options |
| `WriteThrough` | `true` | Write both tiers in parallel (`true`) or L2-first then L1 (`false`) |
| `FallbackToL1OnL2Failure` | `true` | Silently fall back to L1 when L2 throws an exception |

## Architecture

```
GetAsync flow:
  1. Check L1 (memory) → hit → return
  2. Check L2 (Redis)  → hit → populate L1 → return
  3. Both miss          → return Miss

SetAsync flow (write-through):
  L1.SetAsync ─┐
               ├─ await both
  L2.SetAsync ─┘

GetOrSetAsync flow:
  1. Check L1 → hit → return
  2. Check L2 → hit → populate L1 → return
  3. Acquire per-key lock
  4. Double-check L1 (another thread may have populated)
  5. Call factory → SetAsync (both tiers) → return
```

## License

MIT License - see [License.md](License.md) for details.
