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
