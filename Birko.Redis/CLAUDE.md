# Birko.Redis

## Overview
Shared Redis infrastructure for the Birko Framework. Provides `RedisSettings` (extending `RemoteSettings`) and a shared `RedisConnectionManager` used by both `Birko.Caching.Redis` and `Birko.BackgroundJobs.Redis`.

## Structure
```
Birko.Redis/
├── RedisSettings.cs           - Extends RemoteSettings with Database, KeyPrefix, RawConnectionString
└── RedisConnectionManager.cs  - Lazy<ConnectionMultiplexer> singleton, thread-safe
```

## Dependencies
- **Birko.Data.Stores** (imports projitems — for RemoteSettings base class)
- **StackExchange.Redis** NuGet — must be added by the consuming project

## RedisSettings Field Mapping

| Inherited field | Redis meaning | Default |
|---|---|---|
| `Location` | Host | `"localhost"` |
| `Port` | Port | `6379` |
| `Password` | AUTH password | — |
| `UserName` | ACL username (Redis 6+) | — |
| `UseSsl` | TLS connection | `false` |
| `Name` | Client name | — |

New fields:

| Field | Description | Default |
|---|---|---|
| `Database` | Redis DB index (0-15) | `0` |
| `KeyPrefix` | Key namespace isolation | `null` |
| `RawConnectionString` | Override connection string | `null` |

## Key Design Decisions
- `GetConnectionString()` builds a StackExchange.Redis connection string from inherited properties
- `RawConnectionString` bypasses all property-based building for advanced use cases
- `RedisConnectionManager` accepts either `RedisSettings` or a raw connection string
- Connection is lazy-initialized and thread-safe via `Lazy<ConnectionMultiplexer>`

## Consumers
- **Birko.Caching.Redis** — Uses `RedisSettings` + `RedisConnectionManager` for cache backend
- **Birko.BackgroundJobs.Redis** — Uses `RedisSettings` + `RedisConnectionManager` for job queue + lock provider

## Maintenance

### README Updates
When changing RedisSettings properties or RedisConnectionManager API, update README.md.

### CLAUDE.md Updates
When adding/removing files or changing architecture, update the structure tree.
