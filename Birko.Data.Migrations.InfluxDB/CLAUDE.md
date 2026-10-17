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
- `InfluxSchemaBuilder` — CreateCollection creates buckets. AddField and DropIndex are no-ops (schema-less, no
  indexes), and CreateIndex refuses on Build. DropField and RenameField throw `NotSupportedException`, because points
  are immutable and a delete predicate cannot name a field. They were silent no-ops, and the migration was recorded
  (TASK-642).
- **Every bucket lookup and creation goes through `Context/InfluxBucketLookup`** (TASK-612). Bucket names are
  case-sensitive, so the match is exact. The lookup is filtered server-side by organization and name, because an
  unlimited listing is one page of 20 (measured). The `organization` these components take is a **name**, resolved
  to an ID for creation. A name filter that matches nothing is a 404, which the helper reads as "absent".
- **Bookkeeping lives in `birko_migrations`** (`InfluxMigrationStore.DefaultMigrationsBucketName`; override with the
  `bucketName` / `migrationsBucketName` constructor argument). It was `_migrations`, which InfluxDB 2 refuses
  (leading underscore is reserved), so no migration could be recorded (TASK-617). A name starting with `_` is refused
  up front. There is no fallback that adopts an old `_migrations` bucket, because no InfluxDB 2 database can have one.
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
