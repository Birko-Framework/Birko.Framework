# Birko.Caching

Unified caching framework with in-memory implementation for the Birko Framework.

## Features

- ICache interface for cache abstraction
- In-memory cache using ConcurrentDictionary
- CacheEntryOptions (expiration, sliding, priority)
- CacheResult wrapper
- CacheSerializer for serialization

## Installation

```bash
dotnet add package Birko.Caching
```

## Dependencies

- .NET 10.0

## Usage

```csharp
using Birko.Caching;

ICache cache = new MemoryCache();

// Set with options
await cache.SetAsync("key", value, new CacheEntryOptions
{
    AbsoluteExpiration = TimeSpan.FromMinutes(30),
    SlidingExpiration = TimeSpan.FromMinutes(5)
});

// Get
var result = await cache.GetAsync<MyType>("key");
if (result.HasValue)
    Console.WriteLine(result.Value);

// Remove
await cache.RemoveAsync("key");
```

## API Reference

- **ICache** - `GetAsync<T>`, `SetAsync`, `RemoveAsync`, `ExistsAsync`
- **MemoryCache** - In-memory ConcurrentDictionary implementation
- **CacheEntryOptions** - Expiration and priority settings
- **CacheResult\<T\>** - `HasValue`, `Value`
- **CacheSerializer** - Serialization helpers

## Related Projects

- [Birko.Caching.Redis](../Birko.Caching.Redis/) - Redis backend

## License

Part of the Birko Framework.
