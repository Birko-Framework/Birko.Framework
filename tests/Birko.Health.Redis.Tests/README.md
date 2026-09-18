# Birko.Health.Redis.Tests

xUnit + FluentAssertions tests for `Birko.Health.Redis` (`RedisHealthCheck`).

Uses Moq to substitute `IConnectionMultiplexer` / `IDatabase` so the Healthy / Degraded / Unhealthy
branching, the >100ms latency threshold, the `data` dictionary contents, PING faults, and the
constructor null guards are verified without a live Redis server.
