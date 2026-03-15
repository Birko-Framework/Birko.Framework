# Birko.Redis

Shared Redis infrastructure for the Birko Framework. Provides connection settings and connection management used by Redis-based backend projects.

## Features

- **RedisSettings** — Extends `RemoteSettings` with Redis-specific properties (Database, KeyPrefix, RawConnectionString)
- **RedisConnectionManager** — Thread-safe `Lazy<ConnectionMultiplexer>` singleton
- **Settings-based configuration** — Follows the framework's `Settings → PasswordSettings → RemoteSettings` hierarchy

## Dependencies

- Birko.Data.Stores (for RemoteSettings base class)
- StackExchange.Redis (NuGet — added by consuming project)

## Usage

### Settings-based

```csharp
using Birko.Redis;

var settings = new RedisSettings
{
    Location = "redis.example.com",
    Port = 6380,
    Password = "secret",
    UseSsl = true,
    Database = 2,
    KeyPrefix = "myapp"
};

var connectionManager = new RedisConnectionManager(settings);
var db = connectionManager.GetDatabase();
```

### Raw connection string

```csharp
var settings = new RedisSettings
{
    RawConnectionString = "redis.example.com:6380,password=secret,ssl=True",
    KeyPrefix = "myapp"
};

var connectionManager = new RedisConnectionManager(settings);
```

### Direct connection string

```csharp
var connectionManager = new RedisConnectionManager("localhost:6379", database: 0);
```

## API Reference

| Type | Description |
|------|-------------|
| `RedisSettings` | Redis connection settings extending `RemoteSettings` |
| `RedisConnectionManager` | Thread-safe singleton `ConnectionMultiplexer` wrapper |

## Consumers

- [Birko.Caching.Redis](../Birko.Caching.Redis/) — Redis cache backend
- [Birko.BackgroundJobs.Redis](../Birko.BackgroundJobs.Redis/) — Redis job queue and lock provider

## License

Part of the Birko Framework.
