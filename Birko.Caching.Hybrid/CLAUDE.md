# Birko.Caching.Hybrid

## Overview
Two-tier hybrid cache combining L1 (local memory) and L2 (distributed) caches. L1 provides sub-microsecond reads for hot data, L2 ensures consistency across multiple application nodes.

## Structure
```
Birko.Caching.Hybrid/
├── HybridCache.cs              - ICache implementation (L1 + L2 two-tier)
└── HybridCacheOptions.cs       - Configuration (L1 TTL cap, write-through, fallback)
```

## Dependencies
- **Birko.Caching** (imports projitems — ICache, CacheEntryOptions, CacheResult)

No additional NuGet packages required. L1 and L2 cache instances are injected by the caller (typically MemoryCache and RedisCache).

## Key Design Decisions
- **L1 TTL capping** — L1 entries have a configurable max expiration (`L1MaxExpiration`, default 5 min) to limit staleness when L2 is invalidated by another node
- **Write-through** — SetAsync writes both L1 and L2 in parallel by default
- **Fallback resilience** — L2 failures fall back to L1 silently when `FallbackToL1OnL2Failure` is true (default)
- **Stampede prevention** — Per-key `SemaphoreSlim` locks in `GetOrSetAsync` with double-check after lock acquisition
- **No ownership** — HybridCache does NOT dispose L1/L2 caches. The caller owns their lifetime
- **L2-first reads** — GetAsync checks L1 first, falls back to L2, populates L1 on L2 hit

## Usage
```csharp
var l1 = new MemoryCache();
var l2 = new RedisCache(redisSettings);
var options = new HybridCacheOptions
{
    L1DefaultExpiration = TimeSpan.FromSeconds(30),
    L1MaxExpiration = TimeSpan.FromMinutes(5),
    WriteThrough = true,
    FallbackToL1OnL2Failure = true
};
using var cache = new HybridCache(l1, l2, options);

await cache.SetAsync("user:42", user, CacheEntryOptions.Absolute(TimeSpan.FromMinutes(10)));
var result = await cache.GetAsync<User>("user:42");
```

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
