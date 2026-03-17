# Health Checks Guide

## Overview

Birko.Health provides a lightweight health check framework for monitoring application components. It supports concurrent check execution, tag-based filtering (readiness/liveness probes), configurable timeouts, and aggregated reporting.

## Core Concepts

### IHealthCheck

Single-method interface for health checks:

```csharp
public interface IHealthCheck
{
    Task<HealthCheckResult> CheckAsync(CancellationToken ct = default);
}
```

### HealthCheckResult

Readonly struct with static factory methods:

```csharp
// Create results
var healthy = HealthCheckResult.Healthy("DB connection OK");
var degraded = HealthCheckResult.Degraded("High latency: 250ms");
var unhealthy = HealthCheckResult.Unhealthy("Connection refused", exception);

// With metadata
var data = new Dictionary<string, object> { ["latencyMs"] = 42.5 };
var result = HealthCheckResult.Healthy("OK", data);
```

### HealthStatus

Ordered enum: `Healthy (0)` < `Degraded (1)` < `Unhealthy (2)`.

## Health Check Runner

Register checks and run them concurrently:

```csharp
var runner = new HealthCheckRunner(defaultTimeout: TimeSpan.FromSeconds(10))
    .Register("disk", new DiskSpaceHealthCheck("C:\\"), "system", "live")
    .Register("memory", new MemoryHealthCheck(), "system", "live")
    .Register("sql-primary", sqlCheck, "db", "ready")
    .Register("elasticsearch", esCheck, "db")
    .Register("redis", redisCheck, "cache", "ready");

// Run all checks (concurrent)
var report = await runner.RunAsync();

// Run only checks tagged "ready" (for readiness probe)
var readyReport = await runner.RunAsync(tag: "ready");

// Run only checks tagged "live" (for liveness probe)
var liveReport = await runner.RunAsync(tag: "live");
```

### HealthReport

Aggregated result with worst-status:

```csharp
Console.WriteLine($"Overall: {report.Status} ({report.TotalDuration.TotalMilliseconds:F0}ms)");

foreach (var (name, result) in report.Entries)
{
    Console.WriteLine($"  {name}: {result.Status} {result.Duration.TotalMilliseconds:F0}ms — {result.Description}");

    if (result.Data != null)
    {
        foreach (var kv in result.Data)
            Console.WriteLine($"    {kv.Key} = {kv.Value}");
    }
}
```

### Registration Options

```csharp
var reg = new HealthCheckRegistration(
    name: "sql-primary",
    factory: () => new SqlHealthCheck(() => new NpgsqlConnection(connStr)),
    tags: new[] { "db", "ready", "live" },
    timeout: TimeSpan.FromSeconds(3),
    timeoutStatus: HealthStatus.Degraded  // Degraded on timeout instead of Unhealthy
);

runner.Register(reg);
```

## Built-In System Checks

### DiskSpaceHealthCheck

```csharp
// Warning at 1GB free, critical at 256MB free
var check = new DiskSpaceHealthCheck("C:\\", warningThresholdMb: 1024, criticalThresholdMb: 256);
```

Returns data: `drive`, `freeSpaceMb`, `totalSpaceMb`, `freePercent`.

### MemoryHealthCheck

```csharp
// Warning at 1GB working set, critical at 2GB
var check = new MemoryHealthCheck(warningThresholdMb: 1024, criticalThresholdMb: 2048);
```

Returns data: `workingSetMb`, `gcHeapMb`, `totalAvailableMemoryMb`, `gen0Collections`, `gen1Collections`, `gen2Collections`.

## Database Health Checks (Birko.Health.Data)

### SqlHealthCheck

Works with any ADO.NET provider (MSSql, PostgreSQL, MySQL, SQLite, TimescaleDB):

```csharp
// PostgreSQL
var check = new SqlHealthCheck(() => new NpgsqlConnection(connectionString));

// Custom query
var check = new SqlHealthCheck(() => new SqlConnection(connStr), "SELECT GETDATE()");
```

### ElasticSearchHealthCheck

Calls `/_cluster/health` API. Maps cluster status: green=Healthy, yellow=Degraded, red=Unhealthy.

```csharp
var check = new ElasticSearchHealthCheck("http://localhost:9200");
```

### MongoDbHealthCheck

Two modes — custom ping function (for use with MongoDB driver) or simple TCP check:

```csharp
// With MongoDB driver
var check = new MongoDbHealthCheck(async ct =>
{
    await mongoClient.GetDatabase("admin")
        .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
    return true;
});

// Simple TCP connectivity
var check = new MongoDbHealthCheck("mongo-host", 27017);
```

### RavenDbHealthCheck

Calls `/build/version` endpoint:

```csharp
var check = new RavenDbHealthCheck("http://localhost:8080");
```

## Redis Health Check (Birko.Health.Redis)

Sends PING command, measures latency. Degrades above 100ms:

```csharp
var check = new RedisHealthCheck(connectionMultiplexer);

// Or from factory
var check = new RedisHealthCheck(() => connectionManager.GetConnection());
```

Returns data: `latencyMs`, `isConnected`.

## Custom Health Checks

Implement `IHealthCheck`:

```csharp
public class MqttBrokerHealthCheck : IHealthCheck
{
    private readonly MqttMessageQueue _queue;

    public MqttBrokerHealthCheck(MqttMessageQueue queue) => _queue = queue;

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (_queue.IsConnected)
            return Task.FromResult(HealthCheckResult.Healthy("MQTT broker connected"));

        return Task.FromResult(HealthCheckResult.Unhealthy("MQTT broker disconnected"));
    }
}
```

## ASP.NET Core Integration

Wire health checks into endpoints:

```csharp
// In Program.cs or Startup
var healthRunner = new HealthCheckRunner()
    .Register("sql", sqlCheck, "db", "ready")
    .Register("redis", redisCheck, "cache", "ready")
    .Register("disk", diskCheck, "system", "live")
    .Register("memory", memCheck, "system", "live");

app.MapGet("/health", async () =>
{
    var report = await healthRunner.RunAsync();
    return Results.Json(new
    {
        status = report.Status.ToString(),
        duration = $"{report.TotalDuration.TotalMilliseconds:F0}ms",
        checks = report.Entries.ToDictionary(
            e => e.Key,
            e => new { status = e.Value.Status.ToString(), description = e.Value.Description })
    }, statusCode: report.Status == HealthStatus.Healthy ? 200 : 503);
});

app.MapGet("/health/ready", async () =>
{
    var report = await healthRunner.RunAsync(tag: "ready");
    return report.Status == HealthStatus.Healthy ? Results.Ok() : Results.StatusCode(503);
});

app.MapGet("/health/live", async () =>
{
    var report = await healthRunner.RunAsync(tag: "live");
    return report.Status == HealthStatus.Healthy ? Results.Ok() : Results.StatusCode(503);
});
```

## Projects

| Project | Checks | Dependencies |
|---------|--------|-------------|
| `Birko.Health` | DiskSpace, Memory, Runner | None |
| `Birko.Health.Data` | SQL, Elasticsearch, MongoDB, RavenDB | System.Data.Common, System.Net.Http |
| `Birko.Health.Redis` | Redis PING | StackExchange.Redis |

## See Also

- [Birko.Health CLAUDE.md](../../Birko.Health/CLAUDE.md)
- [Birko.Health.Data CLAUDE.md](../../Birko.Health.Data/CLAUDE.md)
- [Birko.Health.Redis CLAUDE.md](../../Birko.Health.Redis/CLAUDE.md)
