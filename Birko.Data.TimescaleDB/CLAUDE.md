# Birko.Data.TimescaleDB

## Overview
TimescaleDB implementation for the Birko data layer providing time-series database storage built on PostgreSQL.

## Project Location
`C:\Source\Birko\Framework\Birko.Data.TimescaleDB\`

## Purpose
- Time-series data storage
- PostgreSQL-compatible
- Hypertables for efficient time-series queries
- Automatic partitioning

## Components

### Connector
- `TimescaleDBConnector` - extends `PostgreSQLConnector` with hypertable support
  (`CreateHypertable`/`CreateHypertableAsync`); both constructors guard null settings (CR-L232)

### Stores
- `TimescaleDBStore<T>` - sync store, bulk operations included (extends `DataBaseBulkStore<TimescaleDBConnector, T>`)
- `AsyncTimescaleDBStore<T>` - async store, bulk operations included (extends `AsyncDataBaseBulkStore<TimescaleDBConnector, T>`)

### Repositories
- `TimescaleDBModelRepository<T>` / `AsyncTimescaleDBModelRepository<T>` - model repositories with
  schema/hypertable helpers; the async schema methods fail fast via a shared `RequireConnector()`
  guard (CR-L233)
- The ViewModel repositories (`TimescaleDBRepository<TViewModel, TModel>` /
  `AsyncTimescaleDBRepository<TViewModel, TModel>`) live in **Birko.Data.TimescaleDB.ViewModel** —
  two bit-rotted, never-compiled copies of them in this project's `Repositories/` folder were
  deleted under CR-L233

## Connection

### Settings (Birko.Data.TimescaleDB.Stores.Settings)
Typed settings extending `SqlSettings` (from Birko.Data.SQL):
- Inherits `CommandTimeout`, `ConnectionTimeout`, abstract `GetConnectionString()` from `SqlSettings`
- `TimeColumn` (default: "timestamp") — column used for hypertable time partitioning
- `ChunkTimeInterval` — chunk time interval for hypertables
- Overrides `GetConnectionString()` with PostgreSQL connection string format

Connection string format (same as PostgreSQL):
```
Host=server_address;Port=5432;Database=database_name;Username=user;Password=password;
```

## Hypertables

TimescaleDB uses hypertables for time-series data:

```sql
CREATE TABLE metrics (
    time TIMESTAMPTZ NOT NULL,
    device_id UUID NOT NULL,
    value DOUBLE PRECISION
);

SELECT create_hypertable('metrics', 'time');
```

## Implementation

```csharp
using Birko.Data.SQL.Stores;
using Birko.Data.SQL.TimescaleDB.Stores;

var store = new AsyncTimescaleDBStore<Metric>();
store.SetSettings(new TimescaleDBSettings("localhost", "metrics", "user", "pass", 5432)
{
    TimeColumn = "timestamp",
    ChunkTimeInterval = "7 days",
});

await store.CreateSchemaAsync();               // CREATE TABLE from the model mapping
await store.CreateHypertableAsync("timestamp"); // convert to a hypertable
var id = await store.CreateAsync(new Metric { /* … */ });
```

## Time Buckets

TimescaleDB can bucket data automatically:

```sql
SELECT time_bucket('5 minutes', time) AS bucket,
       device_id,
       avg(value) AS avg_value
FROM metrics
GROUP BY bucket, device_id
ORDER BY bucket DESC;
```

## Continuous Aggregates

Pre-compute aggregations:

```sql
CREATE MATERIALIZED VIEW metrics_hourly
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 hour', time) AS bucket,
       device_id,
       avg(value) AS avg_value,
       max(value) AS max_value,
       min(value) AS min_value
FROM metrics
GROUP BY bucket, device_id;
```

## Retention Policy

Automatically drop old data:

```sql
SELECT add_retention_policy('metrics', INTERVAL '30 days');
```

## Compression

Compress old chunks to save space:

```sql
ALTER TABLE metrics SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id'
);

SELECT add_compression_policy('metrics', INTERVAL '7 days');
```

## Dependencies
- Birko.Data.Core, Birko.Data.Stores
- Birko.Data.SQL
- Birko.Data.SQL.PostgreSQL
- Npgsql
- TimescaleDB extension for PostgreSQL

## Features

### PostgreSQL Compatible
All PostgreSQL features work:
- Indexes
- Constraints
- Joins
- Transactions
- Replication

### Time-Series Optimizations
- Automatic partitioning
- Efficient time-based queries
- Compression
- Retention policies
- Continuous aggregates

### Functions
- `time_bucket()` - Group by time intervals
- `time_bucket_gapfill()` - Fill missing data
- `locf()` / `interpolate()` - Fill gaps
- `histogram()` - Create histograms

## Best Practices

### Hypertable Design
- Always create hypertables for time-series data
- Choose appropriate time partitioning
- Consider space partitioning for multi-tenant

### Indexes
- Index on time column (automatic with hypertable)
- Index on commonly queried dimensions
- Use composite indexes for common query patterns

### Compression
- Enable compression for old data
- Choose appropriate segmentby columns
- Set compression policy based on retention

### Query Performance
- Always include time range in WHERE clause
- Use time_bucket for aggregations
- Use continuous aggregates for pre-computed data

## Use Cases
- IoT sensor data
- Financial tick data
- Application monitoring
- DevOps metrics
- Environmental monitoring
- Any time-series data with relational needs

## Advantages over InfluxDB
- Full SQL support
- Joins with other tables
- PostgreSQL ecosystem
- No new query language to learn
- ACID transactions

## Migration from PostgreSQL
Easy migration from PostgreSQL:
```sql
-- Add TimescaleDB extension
CREATE EXTENSION IF NOT EXISTS timescaledb;

-- Convert existing table to hypertable
SELECT create_hypertable('existing_table', 'time_column');
```

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
