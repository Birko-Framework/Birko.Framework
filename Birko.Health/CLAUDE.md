# Birko.Health

## Overview

Core health check framework for monitoring application components. Provides `IHealthCheck` interface, aggregated `HealthCheckRunner`, and built-in system checks (disk space, memory).

## Project Location

- **Path:** `C:\Source\Birko.Health\`
- **Type:** Shared project (`.shproj` / `.projitems`)
- **Namespace:** `Birko.Health`, `Birko.Health.Checks`
- **GUID:** `d3e4f5a6-b7c8-4d9e-a0f1-2b3c4d5e6f7a`

## Components

### Core/IHealthCheck.cs
- `IHealthCheck` — single method: `CheckAsync(CancellationToken)`

### Core/HealthCheckResult.cs
- Readonly struct with static factories: `Healthy()`, `Degraded()`, `Unhealthy()`
- Properties: Status, Description, Exception, Data (dictionary), Duration
- `WithDuration()` internal method used by runner

### Core/HealthStatus.cs
- Enum: Healthy (0), Degraded (1), Unhealthy (2)

### Core/HealthCheckRegistration.cs
- Named registration with Factory, Tags, Timeout, TimeoutStatus
- Constructors for factory func or singleton instance

### Core/HealthReport.cs
- Aggregated result: Status (worst of all entries), Entries dictionary, TotalDuration

### Core/HealthCheckRunner.cs
- Registers checks via fluent `Register()` method
- `RunAsync(tag?, ct)` — runs checks concurrently, handles timeouts and exceptions
- Tag-based filtering for readiness/liveness probes
- Concurrent execution via `Task.WhenAll`

### System/DiskSpaceHealthCheck.cs (namespace: Birko.Health.Checks)
- Monitors available disk space with warning/critical thresholds (MB)
- Returns data: drive, freeSpaceMb, totalSpaceMb, freePercent

### System/MemoryHealthCheck.cs (namespace: Birko.Health.Checks)
- Monitors process working set with warning/critical thresholds (MB)
- Returns data: workingSetMb, gcHeapMb, totalAvailableMemoryMb, GC collection counts

## Dependencies

- None (core only)

## Namespace Note

System checks use `Birko.Health.Checks` namespace (not `Birko.Health.System`) to avoid collision with `System.*` in consuming projects.

## Maintenance

- When adding new checks, update this CLAUDE.md and README.md
- All new checks must have unit tests in Birko.Health.Tests
