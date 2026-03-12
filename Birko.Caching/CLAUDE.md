# Birko.Caching

## Overview
Unified caching framework with in-memory implementation. Platform-specific backends (Redis, Hybrid) are separate projects.

## Structure
```
Birko.Caching/
├── Core/
│   ├── ICache.cs              - Get/Set/Remove/Exists/GetOrSet/RemoveByPrefix/Clear async
│   ├── CacheEntryOptions.cs   - AbsoluteExpiration, SlidingExpiration, CachePriority
│   └── CacheResult.cs         - CacheResult<T>: Hit(value) / Miss()
├── Memory/
│   ├── MemoryCache.cs         - ConcurrentDictionary-based, background eviction timer
│   └── MemoryCacheEntry.cs    - Internal entry with expiration tracking
└── Serialization/
    └── CacheSerializer.cs     - System.Text.Json for distributed backends
```

## Dependencies
- None (core only — no external NuGet packages)

## Usage

### Basic
```csharp
using var cache = new Birko.Caching.Memory.MemoryCache();

await cache.SetAsync("key", myObject, CacheEntryOptions.Absolute(TimeSpan.FromMinutes(10)));
var result = await cache.GetAsync<MyType>("key");
if (result.HasValue) { /* use result.Value */ }
```

### GetOrSet (stampede-safe)
```csharp
var product = await cache.GetOrSetAsync("product:123",
    async ct => await db.GetProductAsync(123, ct),
    CacheEntryOptions.Sliding(TimeSpan.FromMinutes(5)));
```

### Prefix removal
```csharp
await cache.RemoveByPrefixAsync("product:");  // Invalidate all product cache entries
```

## Key Design Decisions
- ICache is async-first (distributed backends are inherently async)
- MemoryCache uses per-key SemaphoreSlim in GetOrSetAsync to prevent cache stampede
- Background Timer evicts expired entries (configurable interval, default 60s)
- CachePriority.NeverRemove entries survive eviction sweeps
- CacheResult<T> struct distinguishes "not found" from "found null value"
- CacheSerializer is static — used by Redis and other distributed backends

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
