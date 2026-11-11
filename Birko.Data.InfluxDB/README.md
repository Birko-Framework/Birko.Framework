# Birko.Data.InfluxDB

InfluxDB time-series database implementation for the Birko Framework.

## Features

- Time-series data storage with tags/fields model
- High-performance metrics ingestion (sync/async)
- Flux query language support
- Bulk write operations
- Configurable timestamp precision

## Installation

```bash
dotnet add package Birko.Data.InfluxDB
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- InfluxDB.Client (InfluxDB 2.x)

## Usage

```csharp
using Birko.Data.InfluxDB.Stores;

var settings = new InfluxDBSettings
{
    Url = "http://localhost:8086",
    Token = "your-token",
    Bucket = "my-bucket",
    Organization = "my-org"
};

var store = new InfluxDBStore<Metric>(settings);

// Write a point
var point = PointData.Measurement("temperature")
    .Tag("deviceId", "device-1")
    .Field("value", 23.5)
    .Timestamp(DateTime.UtcNow, WritePrecision.Ms);
```

### Request timeout

`Settings.RequestTimeout` (`TimeSpan?`) bounds every HTTP request a store or repository makes: a read, the read under an
aggregate, a write. Leave it `null` for the InfluxDB client's default of **10 seconds**, or for the `timeout` the
`Location` carries (`http://host:8086?timeout=60000`, in milliseconds). Setting both is refused when the store is
configured; zero, a negative value and anything over ~49.7 days are refused when set (`Timeout.InfiniteTimeSpan` means
no timeout).

```csharp
var settings = new Settings("http://localhost:8086", "my-bucket", "your-token", "my-org")
{
    RequestTimeout = TimeSpan.FromMinutes(2),
};
```

Large reads need it: every entity is its own series (each point carries its `Guid` as a tag), so reading ~100k points
takes 45–50 s on InfluxDB 2 (measured) and fails at the 10 s default — an aggregate too, since it reads every row. A
change of the point layout is TASK-910.

### Flux Query

```
from(bucket: "metrics")
  |> range(start: -1h)
  |> filter(fn: (r) => r._measurement == "temperature")
  |> aggregateWindow(every: 5m, fn: mean)
```

## API Reference

### Stores

- **InfluxDBStore\<T\>** - Sync store
- **InfluxDBBulkStore\<T\>** - Bulk operations
- **AsyncInfluxDBStore\<T\>** - Async store
- **AsyncInfluxDBBulkStore\<T\>** - Async bulk store

### Repositories

- **InfluxDBRepository\<T\>** / **InfluxDBBulkRepository\<T\>**
- **AsyncInfluxDBRepository\<T\>** / **AsyncInfluxDBBulkRepository\<T\>**

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) - Models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) - Store interfaces
- [Birko.Data.InfluxDB.ViewModel](../Birko.Data.InfluxDB.ViewModel/) - ViewModel repositories

## Filter-Based Bulk Operations

Supports filter-based update and delete via default read-modify-save pattern inherited from AbstractBulkStore.

## License

Part of the Birko Framework.
