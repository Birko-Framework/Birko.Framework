# Data Synchronization Guide

## Overview

Birko.Data.Sync provides a framework for synchronizing data between different storage backends. It supports unidirectional and bidirectional sync, conflict resolution, batch processing, and incremental synchronization.

## Core Components

### SyncEntity

Base entity with sync metadata:

```csharp
public class SyncEntity
{
    public DateTime SyncedAt { get; set; }
    public string SyncSource { get; set; }
    public string SyncSourceId { get; set; }
    public long SyncVersion { get; set; }
}
```

### SyncConflict

Represents a conflict detected during bidirectional sync.

### SyncBatch

A batch of sync operations for efficient processing.

## Sync Providers

Provider-specific implementations for each storage backend:

| Provider | Project |
|----------|---------|
| SQL | Birko.Data.Sync.Sql |
| Elasticsearch | Birko.Data.Sync.ElasticSearch |
| MongoDB | Birko.Data.Sync.MongoDb |
| RavenDB | Birko.Data.Sync.RavenDB |
| JSON | Birko.Data.Sync.Json |
| Tenant-aware | Birko.Data.Sync.Tenant |

## Usage

### Basic Sync

```csharp
var sync = new DataSync<Product>(
    sourceStore: sqlStore,
    targetStore: elasticStore,
    syncLog: syncLogStore
);

await sync.SynchronizeAsync();
```

### Configuration

```csharp
sync.Mode = SyncMode.Bidirectional;     // or Unidirectional
sync.ConflictResolution = ConflictResolution.LastWriteWins;
sync.BatchSize = 1000;
```

### Conflict Resolution Strategies

| Strategy | Description |
|----------|-------------|
| `LastWriteWins` | Most recent timestamp wins |
| `SourceWins` | Source store always wins |
| `TargetWins` | Target store always wins |
| Custom | Handle via `OnConflict` event |

### Custom Conflict Resolution

```csharp
sync.OnConflict += (sender, conflict) =>
{
    // Inspect conflict.SourceVersion and conflict.TargetVersion
    // Return the winning version
    return ResolveConflict(conflict);
};
```

## Use Cases

- **Read replicas**: SQL (primary) -> Elasticsearch (search)
- **Cache warming**: SQL -> Elasticsearch or Redis
- **Backup sync**: Primary store -> JSON file backup
- **Multi-region**: Bidirectional sync between regional databases
- **Offline-first**: Local JSON -> Remote SQL when connectivity returns

## Tenant-Aware Sync

`Birko.Data.Sync.Tenant` adds tenant isolation to sync operations, ensuring data is synchronized within tenant boundaries.

## See Also

- [Birko.Data.Sync CLAUDE.md](../Birko.Data.Sync/CLAUDE.md)
- [Birko.Data.Sync.Tenant CLAUDE.md](../Birko.Data.Sync.Tenant/CLAUDE.md)
