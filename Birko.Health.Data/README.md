# Birko.Health.Data

Health checks for database backends — SQL, Elasticsearch, MongoDB, RavenDB.

## Checks

| Check | Method | Healthy When |
|-------|--------|--------------|
| `SqlHealthCheck` | `SELECT 1` via DbConnection | Query succeeds |
| `ElasticSearchHealthCheck` | `/_cluster/health` HTTP API | Cluster green (yellow=Degraded) |
| `MongoDbHealthCheck` | Custom ping func or TCP connect | Ping returns true / TCP connects |
| `RavenDbHealthCheck` | `/build/version` HTTP API | 200 OK |

## Usage

```csharp
// SQL (any provider)
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

// Register with runner
runner.Register("sql-primary", sqlCheck, "db", "ready")
      .Register("elasticsearch", esCheck, "db")
      .Register("mongodb", mongoCheck, "db")
      .Register("ravendb", ravenCheck, "db");
```

## License

Part of the Birko Framework. See [License.md](License.md).
