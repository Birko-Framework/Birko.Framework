# Birko.Data.TimescaleDB

TimescaleDB time-series database implementation built on PostgreSQL for the Birko Framework.

## Features

- Time-series storage with hypertables and automatic partitioning
- Full PostgreSQL compatibility (SQL, joins, transactions)
- Continuous aggregates for pre-computed rollups
- Compression and retention policies
- Time bucket functions for aggregation

## Installation

```bash
dotnet add package Birko.Data.TimescaleDB
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- Birko.Data.SQL
- Birko.Data.SQL.PostgreSQL
- Npgsql

## Usage

```csharp
using Birko.Data.TimescaleDB.Stores;

var store = new TimescaleDBStore<Metric>(settings);
var id = store.Create(metric);
```

### Time Buckets

```sql
SELECT time_bucket('5 minutes', time) AS bucket,
       device_id, avg(value) AS avg_value
FROM metrics
GROUP BY bucket, device_id
ORDER BY bucket DESC;
```

### Continuous Aggregates

```sql
CREATE MATERIALIZED VIEW metrics_hourly
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 hour', time) AS bucket,
       device_id, avg(value), max(value), min(value)
FROM metrics GROUP BY bucket, device_id;
```

## API Reference

### Stores

- **TimescaleDBStore\<T\>** - Sync store with bulk operations (extends `DataBaseBulkStore`)
- **AsyncTimescaleDBStore\<T\>** - Async store with bulk operations (extends `AsyncDataBaseBulkStore`)

### Repositories

- **TimescaleDBModelRepository\<T\>** / **AsyncTimescaleDBModelRepository\<T\>** - model repositories
  with schema/hypertable helpers (`InitAsync`, `CreateSchemaAsync`, `CreateHypertableAsync`, `DropAsync`)
- ViewModel repositories live in [Birko.Data.TimescaleDB.ViewModel](../Birko.Data.TimescaleDB.ViewModel/)
  (`TimescaleDBRepository<TViewModel, TModel>` / `AsyncTimescaleDBRepository<TViewModel, TModel>`)

## Related Projects

- [Birko.Data.SQL.PostgreSQL](../Birko.Data.SQL.PostgreSQL/) - PostgreSQL base
- [Birko.Data.TimescaleDB.ViewModel](../Birko.Data.TimescaleDB.ViewModel/) - ViewModel repositories

## Filter-Based Bulk Operations

Inherits native SQL filter-based operations from `DataBaseBulkStore`/`AsyncDataBaseBulkStore` — `PropertyUpdate<T>` generates a single `UPDATE SET WHERE` and `Delete(filter)` generates a single `DELETE WHERE`.

## License

Part of the Birko Framework.
