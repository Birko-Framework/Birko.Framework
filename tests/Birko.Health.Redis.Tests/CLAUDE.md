# Birko.Health.Redis.Tests

Test project for `Birko.Health.Redis`.

## Scope
- `RedisHealthCheck` — Healthy/Degraded/Unhealthy branching on connection state + PING latency
  (>100ms → Degraded), the `latencyMs`/`isConnected` data entries, PING exception handling, and both
  constructor `ArgumentNullException` guards.

## Conventions
- xUnit + FluentAssertions.
- `IConnectionMultiplexer` / `IDatabase` are mocked via Moq (StackExchange.Redis exposes these
  interfaces specifically to allow substitution) — no live Redis instance is required.
