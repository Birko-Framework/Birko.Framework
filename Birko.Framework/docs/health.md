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

### InfluxDbHealthCheck

Calls `/ping` endpoint. Degrades above 2s:

```csharp
var check = new InfluxDbHealthCheck("http://localhost:8086");
```

### TimescaleDbHealthCheck

TCP connect to PostgreSQL port. Degrades above 2s:

```csharp
var check = new TimescaleDbHealthCheck("timescale-host", 5432);
```

### CosmosDbHealthCheck

HTTP GET to account endpoint (401 expected without auth — endpoint is reachable). Degrades above 2s:

```csharp
var check = new CosmosDbHealthCheck("https://myaccount.documents.azure.com:443");
```

### VaultHealthCheck

Calls `/v1/sys/health`. Maps: 200=Healthy, 429/473=Degraded (standby):

```csharp
var check = new VaultHealthCheck("http://localhost:8200");
```

### MqttHealthCheck

TCP connect to MQTT broker or custom ping function:

```csharp
var check = new MqttHealthCheck("mqtt-host", 1883);
```

### SmtpHealthCheck

TCP connect + reads SMTP 220 banner:

```csharp
var check = new SmtpHealthCheck("smtp-host", 25);
```

### WebSocketHealthCheck

WebSocket handshake or custom ping function:

```csharp
var check = new WebSocketHealthCheck("wss://example.com/ws");
```

### TcpHealthCheck

Generic TCP connect test:

```csharp
var check = new TcpHealthCheck("service-host", 8080);
```

### SseHealthCheck

HTTP GET with `Accept: text/event-stream`:

```csharp
var check = new SseHealthCheck("http://localhost:3000/events");
```

## Mapping Drift Health Check (Birko.Health.Data.ElasticSearch)

`ElasticSearchMappingDriftHealthCheck` runs `DetectMappingDriftAsync()` over the stores you list (one probe each) and
reports fields whose stored mapping differs from the store's (`Missing`, `Unexpected`, `TypeMismatch`). An index
mapped for an older model answers queries with empty results, not errors, so this is the only place that shows it.

```csharp
var check = new ElasticSearchMappingDriftHealthCheck(new Func<CancellationToken, Task<MappingDriftReport>>[]
{
    ct => orderStore.DetectMappingDriftAsync(ct),
});
```

Degraded on drift or on a store it could not ask, never Unhealthy for drift. An index not created yet is Healthy, and
the description says it was not checked.

## Index Declaration Drift Health Check (Birko.Health.Data.IndexManagement)

The document stores declare no indexes on the model: an index on MongoDB, RavenDB or Cosmos DB exists because a host or
a migration created it from an `IndexDefinition`. `IndexDeclarationDriftDetector` (Birko.Data.Patterns) compares that
list with the store's `IIndexManager` and reports, per declared index, `Missing`, `ColumnsDiffer` (other fields, order or
direction) or `UniquenessDiffers`, the kinds SQL schema drift reports. A missing unique index is a correctness failure:
duplicates are accepted silently.

```csharp
var declared = new[]
{
    new IndexDefinition { Name = "UX_Number", Fields = new[] { IndexField.Ascending("Number") }, Unique = true },
    new IndexDefinition { Name = "IX_Customer_Date", Fields = new[] { IndexField.Ascending("CustomerGuid"), IndexField.Descending("Date") } },
};
var check = new IndexDeclarationDriftHealthCheck(new Func<CancellationToken, Task<IndexDeclarationReport>>[]
{
    ct => IndexDeclarationDriftDetector.DetectAsync(new MongoDBIndexManager(client), declared, "Invoices", ct),
});
```

Degraded on drift, and on an index it could not compare (the provider reports no fields for it, or the lookup failed),
never Unhealthy for drift. RavenDB reports the fields of a map its manager generated; a map written by hand is "not
compared". Cosmos DB answers by the names its manager registered (TASK-628); a policy path nobody named is "not
compared". Detection only: nothing is created.

## Stale Grant Health Check (Birko.Health.Security.Grants)

A stored resource grant whose role or resource type was retired from the `GrantRoleTable` grants nothing (TASK-641): it
cannot break the user's checks, and it cannot silently grant either. The cost of that safety is silence, since a role
renamed in configuration removes everyone's access through the old name. `StaleGrantHealthCheck` (TASK-654) is where
it becomes visible, through `StaleGrantDetector.FindAsync`.

```csharp
// After AddBirkoGrants(...), which registers the IResourceGrantStore and the GrantRoleTable:
var check = new StaleGrantHealthCheck(
    sp.GetRequiredService<IResourceGrantStore>(),
    sp.GetRequiredService<GrantRoleTable>());
```

Healthy when none; Degraded when some, with `Data["stale"]` (the full count), `roleNotDeclared`,
`resourceTypeNotDeclared` and `staleGrants` (at most 50 descriptions). Degraded, never Healthy, when the store cannot
be read. Never Unhealthy. The reserved admin grant is never reported, and nothing is migrated or revoked: a person
decides.

## Schema Drift Health Check (Birko.Health.Data.SQL)

Birko never reconciles an existing table: `CREATE TABLE` is guarded by `IF NOT EXISTS` and schema-ensure
only creates. So changing a model — adding `[MaxLengthField]`, changing a decimal's precision, changing a
property's type — leaves the old column in place, and the first sign is an exception on whichever request
touches that column first. This check is what lets somebody find out before that happens.

```csharp
var check = new SchemaDriftHealthCheck(
    () => DataBase.GetConnector<SqLiteConnector>(settings),
    new[] { typeof(Customer), typeof(Invoice) });

var result = await check.CheckAsync(ct);
// Degraded — result.Data["drift"]:
//   ["Invoice.Total: declared TEXT COLLATE BIRKO_DECIMAL, stored REAL"]   (a decimal created before TASK-513)
```

It reports five kinds of disagreement — `TypeMismatch`, `Missing` (declared but absent), `Unexpected`
(present but undeclared), `CaseMismatch` (present only under a spelling that differs in case, on a database where
that makes it unreachable: PostgreSQL always, SQL Server under a case-sensitive collation) and `NullabilityMismatch`
(NULL where the model says NOT NULL, or the reverse; key columns are not compared). Declared indexes are checked too: one
the table lacks, or holds over other columns or without its uniqueness, is counted separately (`indexDrifted`) — and folds in `AbstractConnector.IndexCreationFailures`, the indexes
schema-ensure recorded rather than threw on, which nothing else reads.

**Degraded, never Unhealthy.** Drift means the database disagrees with the models, not that it is
unreachable; reporting it Unhealthy would pull an instance out of a load balancer for a condition only a
human can fix. Reachability is `SqlHealthCheck`'s question.

**"Could not determine" is never reported as healthy.** A provider whose column catalogue this framework
cannot read, or a table that does not exist yet, is called out explicitly rather than counted as clean.

It is a **separate project** from `Birko.Health.Data` so that leaf stays dependency-free — see the comment
in `Birko.Health.Data.SQL.projitems`.

## Redis Health Check (Birko.Health.Redis)

Sends PING command, measures latency. Degrades above 100ms:

```csharp
var check = new RedisHealthCheck(connectionMultiplexer);

// Or from factory
var check = new RedisHealthCheck(() => connectionManager.GetConnection());
```

Returns data: `latencyMs`, `isConnected`.

## InfluxDB Health Check (Birko.Health.Data)

Calls the `/ping` endpoint. Reports latency, degrades above 2 seconds:

```csharp
var check = new InfluxDbHealthCheck("http://localhost:8086");
```

Returns data: `url`, `latencyMs`, `statusCode`.

## Vault Health Check (Birko.Health.Data)

Calls the `/v1/sys/health` endpoint. Maps Vault status codes:

```csharp
var check = new VaultHealthCheck("http://localhost:8200");
```

- **200** = Healthy (active, unsealed)
- **429/473** = Degraded (standby)
- **501/503** = Unhealthy (not initialized / sealed)

Returns data: `url`, `latencyMs`, `statusCode`.

## MQTT Health Check (Birko.Health.Data)

TCP connectivity check or custom ping function:

```csharp
// TCP connect to broker
var check = new MqttHealthCheck("mqtt-broker.local", 1883);

// Custom ping (e.g., from MqttMessageQueue.IsConnected)
var check = new MqttHealthCheck(ct => Task.FromResult(mqttQueue.IsConnected), "MQTT Broker");
```

Returns data: `latencyMs`, `host`, `port`.

## SMTP Health Check (Birko.Health.Data)

TCP connect + SMTP banner verification (expects 220 greeting):

```csharp
var check = new SmtpHealthCheck("smtp.example.com", 587);
```

Returns data: `host`, `port`, `latencyMs`, `banner`.

## Azure Health Checks (Birko.Health.Azure)

### AzureBlobHealthCheck

Probes Azure Blob Storage by listing blobs (maxResults=1). Degrades above 2 seconds:

```csharp
// From existing storage instance
var check = new AzureBlobHealthCheck(azureBlobStorage);

// From factory (DI-friendly)
var check = new AzureBlobHealthCheck(() => serviceProvider.GetRequiredService<AzureBlobStorage>());
```

Returns data: `latencyMs`.

### AzureKeyVaultHealthCheck

Probes Azure Key Vault by listing secrets. Same threshold pattern:

```csharp
var check = new AzureKeyVaultHealthCheck(secretProvider);

// From factory
var check = new AzureKeyVaultHealthCheck(() => serviceProvider.GetRequiredService<AzureKeyVaultSecretProvider>());
```

Returns data: `latencyMs`.

Both checks support the same three statuses:
- **Healthy**: responds within 2 seconds
- **Degraded**: responds but slower than 2 seconds
- **Unhealthy**: connection failed or exception thrown

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
| `Birko.Health.Data` | SQL, Elasticsearch, MongoDB, RavenDB, InfluxDB, Vault, MQTT, SMTP | System.Data.Common, System.Net.Http, System.Net.Sockets |
| `Birko.Health.Data.SQL` | Schema drift + unbuilt indexes | Birko.Data.SQL |
| `Birko.Health.Data.ElasticSearch` | Elasticsearch mapping drift | Birko.Data.ElasticSearch |
| `Birko.Health.Data.IndexManagement` | Index declaration drift (MongoDB, RavenDB, Cosmos DB) | Birko.Data.Patterns |
| `Birko.Health.Redis` | Redis PING | StackExchange.Redis |
| `Birko.Health.Security.Grants` | Stored grants with an undeclared role or resource type | Birko.Security.Grants |
| `Birko.Health.Azure` | Azure Blob Storage, Azure Key Vault | Birko.Storage.AzureBlob, Birko.Security.AzureKeyVault |

## See Also

- [Birko.Health](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Health)
- [Birko.Health.Data](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Health.Data)
- [Birko.Health.Redis](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Health.Redis)
- [Birko.Health.Azure](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Health.Azure)
