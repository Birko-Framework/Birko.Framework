# Birko.Data.SQL.Caching.Tests

## Overview
Unit tests for Birko.Data.SQL.Caching — transparent SQL query caching + invalidation.

## Project Location
`C:\Source\Birko\Framework	ests\Birko.Data.SQL.Caching.Tests\`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- Closes the CR-H085 "no tests" gap. Building this project compile-guards the cached store overrides,
  including the CR-C16 fix (filter-based UpdateAsync/DeleteAsync now invalidate the cache).
- `SqlCacheInvalidationTests` proves the invalidation invariant: every per-query key for a table
  begins with that table prefix, and `MemoryCache.RemoveByPrefixAsync` drops exactly that table’s
  cached queries. The end-to-end filter-write path needs a live SQL backend + model mapping (infra gap).
