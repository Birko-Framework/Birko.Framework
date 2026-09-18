# Birko.Health.Redis

## Overview

Redis health check using StackExchange.Redis PING command.

## Project Location

- **Path:** `C:\Source\Birko.Health.Redis\`
- **Type:** Shared project (`.shproj` / `.projitems`)
- **Namespace:** `Birko.Health.Redis`
- **GUID:** `f5a6b7c8-d9e0-4f1a-b2c3-4d5e6f7a8b9c`

## Components

### RedisHealthCheck.cs
- Takes `IConnectionMultiplexer` or factory `Func<IConnectionMultiplexer>`
- Sends PING, measures latency
- Healthy: connected + latency < 100ms
- Degraded: connected but latency > 100ms
- Unhealthy: not connected or exception
- Returns data: latencyMs, isConnected

## Dependencies

- **Birko.Health** — `IHealthCheck`, `HealthCheckResult`
- **StackExchange.Redis** (NuGet added by consuming project)

## Maintenance

- When changing latency thresholds, update this CLAUDE.md
