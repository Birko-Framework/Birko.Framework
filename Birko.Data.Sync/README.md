# Birko.Data.Sync

Data synchronization framework for keeping data in sync across storage backends in the Birko Framework.

## Features

- Bidirectional and unidirectional sync modes
- Conflict detection and resolution (LastWriteWins, SourceWins, TargetWins, Custom)
- Batch processing with configurable batch size
- Sync metadata tracking (version, source, timestamp)
- Provider-specific implementations

## Installation

```bash
dotnet add package Birko.Data.Sync
```

## Dependencies

- Birko.Data

## Usage

```csharp
using Birko.Data.Sync;

var sync = new DataSync<Product>(
    sourceStore: sqlStore,
    targetStore: elasticStore,
    syncLog: syncLogStore
);

sync.Mode = SyncMode.Bidirectional;
sync.ConflictResolution = ConflictResolution.LastWriteWins;
sync.BatchSize = 1000;

await sync.SynchronizeAsync();
```

## API Reference

### Stores

- **SyncStore\<T\>** / **AsyncSyncStore\<T\>** - Synchronization stores

### Models

- **SyncEntity** - Base entity with sync metadata (SyncedAt, SyncSource, SyncVersion)
- **SyncConflict** - Represents a sync conflict
- **SyncBatch** - Batch of sync operations

## Provider Packages

- [Birko.Data.Sync.Sql](../Birko.Data.Sync.Sql/) - SQL sync
- [Birko.Data.Sync.ElasticSearch](../Birko.Data.Sync.ElasticSearch/) - Elasticsearch sync
- [Birko.Data.Sync.MongoDb](../Birko.Data.Sync.MongoDb/) - MongoDB sync
- [Birko.Data.Sync.RavenDB](../Birko.Data.Sync.RavenDB/) - RavenDB sync
- [Birko.Data.Sync.Json](../Birko.Data.Sync.Json/) - JSON sync
- [Birko.Data.Sync.Tenant](../Birko.Data.Sync.Tenant/) - Tenant-aware sync

## License

Part of the Birko Framework.
