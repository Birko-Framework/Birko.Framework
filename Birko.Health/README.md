# Birko.Health

Health check framework for monitoring application components. Supports concurrent execution, tag-based filtering, configurable timeouts, and aggregated reporting.

## Features

- **IHealthCheck interface** — single `CheckAsync()` method, easy to implement
- **HealthCheckRunner** — concurrent execution, tag filtering, timeout handling
- **HealthReport** — aggregated status (worst-of), per-check results with duration
- **Built-in checks** — disk space, memory usage
- **Extensible** — implement `IHealthCheck` for any component

## Usage

```csharp
var runner = new HealthCheckRunner(defaultTimeout: TimeSpan.FromSeconds(10))
    .Register("disk", new DiskSpaceHealthCheck("C:\\"))
    .Register("memory", new MemoryHealthCheck())
    .Register("sql", sqlCheck, "db", "ready")
    .Register("redis", redisCheck, "cache", "ready")
    .Register("elasticsearch", esCheck, "db");

// Run all checks
var report = await runner.RunAsync();
// report.Status: Healthy | Degraded | Unhealthy

// Run only "db" tagged checks
var dbReport = await runner.RunAsync(tag: "db");

// Inspect individual results
foreach (var (name, result) in report.Entries)
{
    Console.WriteLine($"{name}: {result.Status} ({result.Duration.TotalMilliseconds:F0}ms)");
}
```

## API Reference

| Type | Description |
|------|-------------|
| `IHealthCheck` | Interface: `CheckAsync(CancellationToken)` |
| `HealthCheckResult` | Readonly struct: `Healthy()` / `Degraded()` / `Unhealthy()` with Description, Exception, Data |
| `HealthStatus` | Enum: Healthy, Degraded, Unhealthy |
| `HealthCheckRegistration` | Name + Factory + Tags + Timeout |
| `HealthCheckRunner` | Register checks, `RunAsync(tag?, ct)` |
| `HealthReport` | Aggregated Status, Entries, TotalDuration |
| `DiskSpaceHealthCheck` | Disk free space (warning/critical MB thresholds) |
| `MemoryHealthCheck` | Process working set (warning/critical MB thresholds) |

## License

Part of the Birko Framework. See [License.md](License.md).
