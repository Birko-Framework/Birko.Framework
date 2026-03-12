# Birko.Caching.Redis

Redis-backed ICache implementation for the Birko Framework.

## Features

- RedisCache implementing ICache interface
- Thread-safe singleton RedisConnectionManager
- Configurable Redis connection options
- Distributed caching for multi-instance deployments

## Installation

```bash
dotnet add package Birko.Caching.Redis
```

## Dependencies

- Birko.Caching
- StackExchange.Redis

## Usage

```csharp
using Birko.Caching.Redis;

var options = new RedisCacheOptions
{
    ConnectionString = "localhost:6379",
    InstanceName = "myapp:"
};

ICache cache = new RedisCache(options);
await cache.SetAsync("key", value);
var result = await cache.GetAsync<MyType>("key");
```

## API Reference

- **RedisCache** - Redis ICache implementation
- **RedisCacheOptions** - Connection string, instance name
- **RedisConnectionManager** - Thread-safe singleton connection manager

## Related Projects

- [Birko.Caching](../Birko.Caching/) - Core caching interfaces

## License

Part of the Birko Framework.
