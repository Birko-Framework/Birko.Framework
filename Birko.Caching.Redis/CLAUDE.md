# Birko.Caching.Redis

## Overview
Redis-backed ICache implementation using StackExchange.Redis.

## Structure
```
Birko.Caching.Redis/
├── RedisCache.cs              - ICache implementation over Redis
├── RedisCacheOptions.cs       - ConnectionString, InstanceName, DefaultExpiration, Database
└── RedisConnectionManager.cs  - Lazy<ConnectionMultiplexer> singleton, thread-safe
```

## Dependencies
- **Birko.Caching** (imports projitems)
- **StackExchange.Redis** NuGet — must be added by the consuming project

## Usage
```csharp
var options = new RedisCacheOptions
{
    ConnectionString = "localhost:6379",
    InstanceName = "symbio",    // Keys prefixed as "symbio:{key}"
    DefaultExpiration = TimeSpan.FromMinutes(10),
    Database = 0
};
using var cache = new RedisCache(options);

await cache.SetAsync("user:42", user, CacheEntryOptions.Sliding(TimeSpan.FromMinutes(15)));
var result = await cache.GetAsync<User>("user:42");
```

## Key Design Decisions
- RedisConnectionManager wraps Lazy<ConnectionMultiplexer> — safe to share across threads
- Sliding expiration uses Redis Hash metadata key (`{key}:__meta`) to track sliding/absolute TTL
- GetOrSetAsync uses Redis SET NX as distributed lock to prevent stampede
- RemoveByPrefixAsync uses SCAN via KeysAsync (non-blocking)
- ClearAsync with InstanceName does prefix removal; without it does FlushDatabase
- All values serialized via CacheSerializer (JSON)

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
