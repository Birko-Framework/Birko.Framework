# Birko.Data.Migrations.InfluxDB

## Overview
InfluxDB migration backend using InfluxDBClient. Implements platform-agnostic IMigrationContext.

## Project Location
`Birko.Data.Migrations.InfluxDB/`

## Components

### Runner
- `InfluxMigrationRunner` — Takes `InfluxDBClient` and organization name.

### Context
- `InfluxMigrationContext` — Wraps InfluxDBClient + organization. Schema and Data properties. Raw() exposes InfluxDBClient.
- `InfluxSchemaBuilder` — CreateCollection creates buckets. AddField/DropField/CreateIndex are no-op (time-series).
- **Every bucket lookup and creation goes through `Context/InfluxBucketLookup`** (TASK-612). Bucket names are
  case-sensitive, so the match is exact. The lookup is filtered server-side by organization and name, because an
  unlimited listing is one page of 20 (measured). The `organization` these components take is a **name**, resolved
  to an ID for creation. A name filter that matches nothing is a 404, which the helper reads as "absent".
- `InfluxDataMigrator` — DeleteDocuments via DeleteApi, CountDocuments via Flux count().

### Store
- `InfluxMigrationStore` — Stores migration metadata in InfluxDB measurements via Flux queries.

## Usage

```csharp
var runner = new InfluxMigrationRunner(store.Client, "my-org");
runner.Register(new CreateBucket());
runner.Migrate();
```

## Dependencies
- Birko.Data.Migrations
- Birko.Data.Patterns
- Birko.Data.InfluxDB (InfluxDBClient)
- InfluxDB.Client

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
