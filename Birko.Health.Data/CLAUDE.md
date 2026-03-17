# Birko.Health.Data

## Overview

Health checks for database backends. Each check tests connectivity using the simplest possible operation (SQL query, HTTP ping, TCP connect).

## Project Location

- **Path:** `C:\Source\Birko.Health.Data\`
- **Type:** Shared project (`.shproj` / `.projitems`)
- **Namespace:** `Birko.Health.Data`
- **GUID:** `e4f5a6b7-c8d9-4e0f-b1a2-3c4d5e6f7a8b`

## Components

### SqlHealthCheck.cs
- Takes `Func<DbConnection>` factory + optional query (default "SELECT 1")
- Opens connection, executes scalar, reports success/failure
- Works with any ADO.NET provider (MSSql, PostgreSQL, MySQL, SQLite, TimescaleDB)

### ElasticSearchHealthCheck.cs
- Takes base URL + optional HttpClient
- Calls `/_cluster/health` API
- Maps cluster status: green=Healthy, yellow=Degraded, red=Unhealthy

### MongoDbHealthCheck.cs
- Two constructors: custom ping func or host+port TCP check
- Custom func: `Func<CancellationToken, Task<bool>>` for use with MongoDB driver
- TCP: simple connection test to verify reachability

### RavenDbHealthCheck.cs
- Takes base URL + optional HttpClient
- Calls `/build/version` endpoint
- Reports success based on HTTP status code

## Dependencies

- **Birko.Health** — `IHealthCheck`, `HealthCheckResult`
- **System.Data.Common** (for SqlHealthCheck DbConnection)
- **System.Net.Http** (for ES/RavenDB HttpClient)

## Maintenance

- When adding new data backend checks, add to this project and update .projitems
