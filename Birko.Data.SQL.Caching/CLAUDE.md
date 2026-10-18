# Birko.Data.SQL.Caching

## Overview
Query caching for Birko SQL stores: an async bulk SQL store subclass with transparent ICache read caching and invalidation on writes.

## Project Location
`Birko.Data.SQL.Caching/`

## Components

### CachedAsyncDataBaseBulkStore\<DB,T\>
Subclass of `AsyncDataBaseBulkStore<DB,T>` (overrides the `*Core` seams) that:
- Caches both `ReadCoreAsync` overloads as a JSON copy (TASK-169: never a shared instance; a not-found single read is
  cached as `null`; a type whose mapped property has no public getter/setter is not cached)
- Does not read or fill the cache inside a transaction (`TransactionContext` or ambient), nor while a read-then-write
  `UpdateAsync(filter, Action)` runs (SH-H007)
- Invalidates the table prefix after every write, including `DestroyAsync`, with no cancellation token and also when the
  write threw. Inside a transaction it also registers a second invalidation that the boundary's owner runs after the
  commit (`AmbientSqlTransaction.OnCommitted`, TASK-669); a `DbTransaction` committed by the caller itself (no
  `SqlUnitOfWork`) has no owner to run it

### SqlCacheKeyBuilder
- `BuildKey` (collection reads) and `BuildSingleKey` (single reads, `:one` suffix): SHA256 of the value-distinguishing
  filter description from `TryDescribeFilter`
- `GetTablePrefix(scope, table)`: `sql:{database id}:{table}:`, the invalidation prefix

### SqlCacheOptions
- `DefaultExpiration` (5 min): absolute TTL of a cached read
- `Enabled` (true)

## Dependencies
- Birko.Data.SQL (AsyncDataBaseBulkStore, AbstractConnector)
- Birko.Caching (ICache, CacheEntryOptions, RemoveByPrefixAsync)

## Key Notes
- Consumers need both Birko.Data.SQL and Birko.Caching shared project imports
- As a subclass it keeps the full store interface — callers do not need to know about caching
- Cache invalidation uses `RemoveByPrefixAsync` with the table prefix to clear all related entries on writes, with `CancellationToken.None`
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
