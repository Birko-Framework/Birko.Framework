# Birko.Health.Tests

## Overview

Unit tests for Birko.Health, Birko.Health.Data, and Birko.Health.Redis.

## Project Location

- **Path:** `C:\Source\Birko.Health.Tests\`
- **Type:** Test project (`.csproj`, xUnit)
- **Target:** net10.0

## Test Files

- **HealthStatusTests.cs** — Enum ordering and values
- **HealthCheckResultTests.cs** — Static factories, properties, WithDuration
- **HealthCheckRegistrationTests.cs** — Constructor validation, factory/instance
- **HealthReportTests.cs** — Worst-status aggregation, empty report
- **HealthCheckRunnerTests.cs** — Concurrent execution, tag filtering, timeout, exception handling, fluent API
- **SystemHealthCheckTests.cs** — DiskSpace and Memory checks on current system
- **DataHealthCheckTests.cs** — SQL/ES/MongoDB/RavenDB constructor validation and error handling

## Dependencies

- Birko.Health, Birko.Health.Data, Birko.Health.Redis (.projitems)
- xUnit 2.9.3, FluentAssertions 7.0.0, StackExchange.Redis 2.8.24
