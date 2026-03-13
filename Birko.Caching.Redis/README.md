# Birko.Caching.Redis

Redis-backed ICache implementation for the Birko Framework.

## Features

- RedisCache implementing ICache interface
- Shared `RedisConnectionManager` from `Birko.Redis`
- `RedisSettings` configuration (extends framework's `RemoteSettings` hierarchy)
- Distributed caching for multi-instance deployments
- Connection ownership tracking (safe shared connections)

## Dependencies

- Birko.Caching
- Birko.Redis (RedisSettings, RedisConnectionManager)
- StackExchange.Redis

## Usage

```csharp
using Birko.Caching.Redis;
using Birko.Redis;

var settings = new RedisSettings
{
    Location = "localhost",
    Port = 6379,
    KeyPrefix = "myapp",        // Keys prefixed as "myapp:{key}"
    Database = 0
};

ICache cache = new RedisCache(settings, defaultExpiration: TimeSpan.FromMinutes(5));
await cache.SetAsync("key", value);
var result = await cache.GetAsync<MyType>("key");
```

### Shared Connection

```csharp
var connectionManager = new RedisConnectionManager(settings);

// Both cache instances share one connection
var cache1 = new RedisCache(connectionManager, settings);
var cache2 = new RedisCache(connectionManager, settings);
```

## API Reference

- **RedisCache** — Redis ICache implementation

## Related Projects

- [Birko.Caching](../Birko.Caching/) — Core caching interfaces
- [Birko.Redis](../Birko.Redis/) — Shared Redis settings and connection manager

## License

Part of the Birko Framework.
