# Birko.Data.SQL.Caching

## Overview
Query caching layer for Birko SQL stores using the decorator pattern. Wraps async SQL stores with transparent ICache integration for read-through caching and automatic write-through invalidation.

## Project Location
`Birko.Data.SQL.Caching/`

## Components

### CachedAsyncDataBaseBulkStore\<DB,T\>
Decorator around `AsyncDataBaseBulkStore<DB,T>` that:
- Intercepts read operations (Read, List, Select) to check cache first
- Delegates to the inner store on cache miss
- Invalidates cache entries by table prefix on write operations (Insert, Update, Delete)

### SqlCacheKeyBuilder
Static utility for deterministic cache key generation:
- Combines table name, query text, and parameter values
- Produces SHA256 hash for consistent, collision-resistant keys
- Supports prefix-based grouping for bulk invalidation

### SqlCacheOptions
Configuration for the caching decorator:
- `DefaultExpiration` — cache entry TTL
- `TablePrefix` — prefix used for cache key grouping and invalidation
- `InvalidateOnWrite` — whether writes trigger prefix-based cache removal

## Dependencies
- Birko.Data.SQL (AsyncDataBaseBulkStore, AbstractConnector)
- Birko.Caching (ICache, CacheEntryOptions, RemoveByPrefixAsync)

## Key Notes
- Consumers need both Birko.Data.SQL and Birko.Caching shared project imports
- The decorator pattern preserves the full store interface — callers do not need to know about caching
- Cache invalidation uses `RemoveByPrefixAsync` with the table prefix to clear all related entries on writes
- SqlCacheKeyBuilder uses SHA256 to keep keys short and deterministic regardless of query complexity

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns, update README.md.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
