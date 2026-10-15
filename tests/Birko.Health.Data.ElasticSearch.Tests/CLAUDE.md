# Birko.Health.Data.ElasticSearch.Tests

Tests for `ElasticSearchMappingDriftHealthCheck` (TASK-624).

- **Offline:** one test per answer a probe can give (clean, drift, unsupported, index not created yet, a throwing probe),
  using stub `MappingDriftReport`s.
- **Live:** a real store over an index mapped for an older model is Degraded. Gated on `BIRKO_ES_URL`;
  `BIRKO_REQUIRE_LIVE` makes its absence a failure.

xUnit + FluentAssertions. Run with `dotnet test --nologo`.
