# Birko.Health.Data

## Overview

Health checks for infrastructure backends — databases, message brokers, secret vaults, and email servers. Each check tests connectivity using the simplest possible operation (SQL query, HTTP ping, TCP connect).

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

### InfluxDbHealthCheck.cs
- Takes base URL + optional HttpClient
- Calls `/ping` endpoint
- Reports latency, degrades above 2s

### VaultHealthCheck.cs
- Takes base URL + optional HttpClient
- Calls `/v1/sys/health` endpoint
- Maps Vault status codes: 200=Healthy, 429/473=Degraded (standby), other=Unhealthy

### MqttHealthCheck.cs
- Two constructors: host+port TCP check or custom ping func
- TCP: connects to MQTT broker port (default 1883)
- Custom func: `Func<CancellationToken, Task<bool>>` for use with MqttMessageQueue.IsConnected
- Reports latency, degrades above 2s

### SmtpHealthCheck.cs
- Takes host + port (default 25)
- TCP connect, reads SMTP 220 banner, sends QUIT
- Reports banner text and latency, degrades above 2s

### WebSocketHealthCheck.cs
- Two constructors: URI string or custom ping func
- URI: performs WebSocket handshake (HTTP Upgrade), then closes gracefully
- Custom func: `Func<CancellationToken, Task<bool>>` for use with existing connection
- Reports latency, degrades above 2s

### TcpHealthCheck.cs
- Takes host + port (1–65535)
- Simple TCP connect test, no protocol-specific logic
- Reports latency, degrades above 2s

### CosmosDbHealthCheck.cs
- Takes base URL (account endpoint) + optional HttpClient
- HTTP GET to endpoint — 401 is expected without auth (endpoint reachable)
- Reports latency, degrades above 2s

### TimescaleDbHealthCheck.cs
- Takes host + port (default 5432, PostgreSQL)
- TCP connect test to verify TimescaleDB/PostgreSQL reachability
- Reports latency, degrades above 2s

### SseHealthCheck.cs
- Takes URL + optional HttpClient
- Sends HTTP GET with `Accept: text/event-stream`, reads response headers only
- Verifies response content type is `text/event-stream` (Degraded if wrong)
- Reports latency, degrades above 2s

## Dependencies

- **Birko.Health** — `IHealthCheck`, `HealthCheckResult`
- **System.Data.Common** (for SqlHealthCheck DbConnection)
- **System.Net.Http** (for ES/RavenDB/InfluxDB/Vault HttpClient)
- **System.Net.Sockets** (for MongoDB/MQTT/SMTP TCP checks)
- **System.Net.WebSockets** (for WebSocket handshake check)

## Maintenance

- When adding new infrastructure backend checks, add to this project and update .projitems
