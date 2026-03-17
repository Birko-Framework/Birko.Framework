# Birko.Caching.Hybrid.Tests

Unit tests for the Birko.Caching.Hybrid project.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0

## Test Coverage

- `HybridCacheTests` — L1/L2 read flow, write-through, populate L1 from L2, remove/clear both tiers, prefix removal, GetOrSetAsync
- `HybridCacheFallbackTests` — L2 failure resilience, fallback to L1, exception propagation when fallback disabled
- `HybridCacheOptionsTests` — Default option values

## Running Tests

```bash
dotnet test
```

## License

MIT License - see [License.md](License.md) for details.
