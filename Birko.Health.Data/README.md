# Birko.Health.Data

Health checks for database backends, messaging infrastructure, and network services.

## Checks

| Check | Method | Healthy When |
|-------|--------|--------------|
| `SqlHealthCheck` | `SELECT 1` via DbConnection | Query succeeds |
| `ElasticSearchHealthCheck` | `/_cluster/health` HTTP API | Cluster green (yellow=Degraded) |
| `MongoDbHealthCheck` | Custom ping func or TCP connect | Ping returns true / TCP connects |
| `RavenDbHealthCheck` | `/build/version` HTTP API | 200 OK |
| `InfluxDbHealthCheck` | `/ping` HTTP API | 200 OK (Degraded if >2s) |
| `TimescaleDbHealthCheck` | TCP connect to PostgreSQL port | TCP connects (Degraded if >2s) |
| `CosmosDbHealthCheck` | HTTP GET to account endpoint | Reachable (401 OK — Degraded if >2s) |
| `VaultHealthCheck` | `/v1/sys/health` HTTP API | 200=Healthy, 429/473=Degraded |
| `MqttHealthCheck` | TCP connect or custom ping func | TCP connects / Ping returns true |
| `SmtpHealthCheck` | TCP connect + SMTP 220 banner | Banner starts with "220" |
| `WebSocketHealthCheck` | WebSocket handshake or custom func | Handshake succeeds (Degraded if >2s) |
| `TcpHealthCheck` | TCP connect to host:port | TCP connects (Degraded if >2s) |
| `SseHealthCheck` | HTTP GET with `Accept: text/event-stream` | Content-Type matches (Degraded if >2s) |

## Usage

```csharp
// SQL (any ADO.NET provider)
var sqlCheck = new SqlHealthCheck(() => new NpgsqlConnection(connectionString));

// Elasticsearch
var esCheck = new ElasticSearchHealthCheck("http://localhost:9200");

// MongoDB (with driver)
var mongoCheck = new MongoDbHealthCheck(async ct =>
{
    await mongoClient.GetDatabase("admin").RunCommandAsync<BsonDocument>(
        new BsonDocument("ping", 1), cancellationToken: ct);
    return true;
});

// MongoDB (simple TCP)
var mongoTcpCheck = new MongoDbHealthCheck("mongo-host", 27017);

// RavenDB
var ravenCheck = new RavenDbHealthCheck("http://localhost:8080");

// InfluxDB
var influxCheck = new InfluxDbHealthCheck("http://localhost:8086");

// TimescaleDB (PostgreSQL extension)
var tsCheck = new TimescaleDbHealthCheck("timescale-host", 5432);

// Cosmos DB
var cosmosCheck = new CosmosDbHealthCheck("https://myaccount.documents.azure.com:443");

// Vault
var vaultCheck = new VaultHealthCheck("http://localhost:8200");

// MQTT
var mqttCheck = new MqttHealthCheck("mqtt-host", 1883);

// SMTP
var smtpCheck = new SmtpHealthCheck("smtp-host", 25);

// WebSocket
var wsCheck = new WebSocketHealthCheck("wss://example.com/ws");

// TCP (generic)
var tcpCheck = new TcpHealthCheck("service-host", 8080);

// SSE
var sseCheck = new SseHealthCheck("http://localhost:3000/events");

// Register with runner
runner.Register("sql-primary", sqlCheck, "db", "ready")
      .Register("elasticsearch", esCheck, "db")
      .Register("mongodb", mongoCheck, "db")
      .Register("ravendb", ravenCheck, "db")
      .Register("influxdb", influxCheck, "db")
      .Register("cosmosdb", cosmosCheck, "db")
      .Register("vault", vaultCheck, "infra")
      .Register("mqtt", mqttCheck, "messaging");
```

## License

Part of the Birko Framework. See [License.md](License.md).
