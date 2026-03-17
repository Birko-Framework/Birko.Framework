# Birko.Caching.Hybrid.Tests

## Overview
Unit tests for Birko.Caching.Hybrid — two-tier L1/L2 cache.

## Project Location
`C:\Source\Birko.Caching.Hybrid.Tests\`

## Structure
```
Birko.Caching.Hybrid.Tests/
└── HybridCacheTests.cs        - All tests (HybridCacheTests, HybridCacheFallbackTests, HybridCacheOptionsTests, FailingCache helper)
```

## Dependencies
- **Birko.Caching** (imports projitems)
- **Birko.Caching.Hybrid** (imports projitems)
- xUnit 2.9.3, FluentAssertions 7.0.0

## Test Approach
- Uses two `MemoryCache` instances as L1 and L2 (no Redis dependency)
- `FailingCache` helper simulates L2 failures for fallback tests

## Maintenance
When adding new features to Birko.Caching.Hybrid, add corresponding tests here covering success, failure, and edge cases.
