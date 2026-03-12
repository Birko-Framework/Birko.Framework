# Birko.Data.InfluxDB

## Overview
InfluxDB implementation for Birko.Data providing time-series database storage.

## Project Location
`C:\Source\Birko.Data.InfluxDB\`

## Purpose
- Time-series data storage
- High-performance metrics and monitoring
- Efficient data compression
- Optimized for time-based queries

## Components

### Stores
- `InfluxDBStore<T>` - Synchronous InfluxDB store
- `InfluxDBBulkStore<T>` - Bulk operations store
- `AsyncInfluxDBStore<T>` - Asynchronous InfluxDB store
- `AsyncInfluxDBBulkStore<T>` - Async bulk operations store

### Repositories
- `InfluxDBRepository<T>` - InfluxDB repository
- `InfluxDBBulkRepository<T>` - Bulk repository
- `AsyncInfluxDBRepository<T>` - Async repository
- `AsyncInfluxDBBulkRepository<T>` - Async bulk repository

## Connection

Connection settings:
```csharp
var settings = new InfluxDBSettings
{
    Url = "http://localhost:8086",
    Token = "your-token",
    Bucket = "my-bucket",
    Organization = "my-org"
};
```

## Data Model

InfluxDB uses a different data model:
- **Measurement** - Similar to a table
- **Tag** - Indexed string metadata
- **Field** - Data values (not indexed)
- **Timestamp** - Time associated with the data point

## Implementation

```csharp
using Birko.Data.InfluxDB.Stores;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;

public class MetricStore : InfluxDBStore<Metric>
{
    public MetricStore(InfluxDBSettings settings) : base(settings)
    {
    }

    public override Guid Create(Metric item)
    {
        var point = PointData.Measurement(Settings.Measurement)
            .Tag("deviceId", item.DeviceId.ToString())
            .Field("value", item.Value)
            .Timestamp(item.Timestamp, WritePrecision.Ns);

        Client.GetWriteApiAsync().WritePoint(point, Settings.Bucket, Settings.Org);
        return item.Id;
    }
}
```

## Querying

```csharp
using (var table = Client.GetQueryApi().Query(
    $"from(bucket: \"{Settings.Bucket}\") " +
    $"|> range(start: -1h) " +
    $"|> filter(fn: (r) => r._measurement == \"{Settings.Measurement}\")",
    Settings.Org))
{
    foreach (var record in table.Records)
    {
        // Process records
    }
}
```

## Flux Query Language

InfluxDB uses Flux for queries:

```
from(bucket: "metrics")
  |> range(start: -1h)
  |> filter(fn: (r) => r._measurement == "temperature")
  |> filter(fn: (r) => r.device == "device-1")
  |> aggregateWindow(every: 5m, fn: mean)
  |> yield(name: "avg")
```

## Bulk Operations

```csharp
public override IEnumerable<KeyValuePair<Metric, Guid>> CreateAll(IEnumerable<Metric> items)
{
    var points = items.Select(item =>
        PointData.Measurement(Settings.Measurement)
            .Tag("deviceId", item.DeviceId.ToString())
            .Field("value", item.Value)
            .Timestamp(item.Timestamp, WritePrecision.Ns)
    );

    Client.GetWriteApiAsync().WritePoints(points, Settings.Bucket, Settings.Org);
    return items.Select(item => new KeyValuePair<Metric, Guid>(item, item.Id));
}
```

## Dependencies
- Birko.Data
- InfluxDB.Client (official InfluxDB 2.x .NET client)
- InfluxDB Server 2.x

## Data Types

Supported field types:
- `float` - 64-bit floating-point (default)
- `integer` - 64-bit integer
- `unsigned` - 64-bit unsigned integer
- `boolean` - true/false
- `string` - String value

Tags are always strings.

## Best Practices

### Tags vs Fields
- Use **tags** for indexed data (device IDs, locations, types)
- Use **fields** for actual measurements (temperature, value, count)
- Tags are indexed but limited cardinality
- Fields are not indexed but support high cardinality

### Timestamp Precision
Choose appropriate precision:
- `Ns` - Nanoseconds (highest precision)
- `Us` - Microseconds
- `Ms` - Milliseconds
- `S` - Seconds

### Batch Size
- Optimal batch size: 5,000-10,000 points
- Larger batches = better compression
- Don't exceed InfluxDB write limits

### Query Performance
- Always include time range in queries
- Use tags for filtering
- Aggregate data for large time ranges

## Use Cases
- IoT sensor data
- Application metrics
- Performance monitoring
- Financial tick data
- Environmental monitoring
- DevOps monitoring

## Limitations
- Not a general-purpose database
- No joins
- Limited data types
- Time-based queries only
- Tag cardinality limits

## InfluxDB 1.x vs 2.x

This implementation targets InfluxDB 2.x with:
- Flux query language
- Token-based authentication
- Buckets instead of databases
- Built-in UI and task management

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
