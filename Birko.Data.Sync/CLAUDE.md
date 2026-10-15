# Birko.Data.Sync

## Overview
Data synchronization framework for keeping data in sync across different storage backends.

## Project Location
`Birko.Data.Sync/`

## Purpose
- Synchronize data between different databases
- Multi-master replication
- Conflict detection and resolution
- Batch processing

## Components

### Stores
- `SyncStore<T>` - Synchronization store
- `AsyncSyncStore<T>` - Async synchronization store

### Models
- `SyncEntity` - Base entity with sync metadata
- `SyncConflict` - Represents a sync conflict
- `SyncBatch` - Batch of sync operations

### Sync Providers
- `SyncProvider<TStore, T, TKnowledge>` / `AsyncSyncProvider<…>` - the engine: one provider for any pair of bulk
  stores; the backend-specific part is only the knowledge store (Birko.Data.Sync.Sql / .ElasticSearch / .MongoDb /
  .Json / .Xml)
- `SyncProviderBase<T, TKnowledge>` (Internal) - the decision both providers share
- `SyncQueue` - one run per scope at a time, plus a global concurrency limit

## Sync Architecture

```
Local Store ⇄ Sync Engine ⇄ Remote Store
                   ↕
            Knowledge Store
```

The decision rests on a **known pair**: a knowledge row recorded while both sides held the item (TASK-157). A deletion
is a known pair with one side missing; unchanged is Skip; one side changed wins; both changed is the policy. Knowledge
is written only for a reconciled state, and each run writes a scope marker row (`EntityGuid = Guid.Empty`). Never read
`IsLocalDeleted` / `IsRemoteDeleted` as "deleted": they mean "this side had no version when the row was written".
See docs/sync.md.

## Creating a Sync Operation

```csharp
using Birko.Data.Sync;

var sync = new DataSync<Product>(
    sourceStore: sqlStore,
    targetStore: elasticStore,
    syncLog: syncLogStore
);

await sync.SynchronizeAsync();
```

## Sync Strategies

### Bidirectional Sync
```csharp
sync.Mode = SyncMode.Bidirectional;
sync.ConflictResolution = ConflictResolution.LastWriteWins;
```

### Unidirectional Sync
```csharp
sync.Mode = SyncMode.SourceToTarget; // One-way replication
```

### Batch Sync
```csharp
sync.BatchSize = 1000; // Process in batches
```

## Conflict Resolution

### Last Write Wins
```csharp
sync.ConflictResolution = ConflictResolution.LastWriteWins;
```

### Source Wins
```csharp
sync.ConflictResolution = ConflictResolution.SourceWins;
```

### Target Wins
```csharp
sync.ConflictResolution = ConflictResolution.TargetWins;
```

### Custom Resolution
```csharp
sync.OnConflict += (sender, conflict) =>
{
    // Custom logic
    return ResolveConflict(conflict);
};
```

## Sync Metadata

Each entity tracks sync information:

```csharp
public class SyncEntity : Entity
{
    public DateTime? SyncedAt { get; set; }
    public string SyncSource { get; set; }
    public Guid? SyncSourceId { get; set; }
    public long SyncVersion { get; set; }
}
```

## Dependencies
- Birko.Data.Core, Birko.Data.Stores

## Provider-Specific Sync

Different providers have their own sync implementations:
- [Birko.Data.Sync.Sql](../Birko.Data.Sync.Sql/CLAUDE.md) - SQL sync
- [Birko.Data.Sync.ElasticSearch](../Birko.Data.Sync.ElasticSearch/CLAUDE.md) - Elasticsearch sync
- [Birko.Data.Sync.MongoDb](../Birko.Data.Sync.MongoDb/CLAUDE.md) - MongoDB sync
- [Birko.Data.Sync.RavenDB](../Birko.Data.Sync.RavenDB/CLAUDE.md) - RavenDB sync
- [Birko.Data.Sync.Json](../Birko.Data.Sync.Json/CLAUDE.md) - JSON sync
- [Birko.Data.Sync.Tenant](../Birko.Data.Sync.Tenant/CLAUDE.md) - Tenant-aware sync

## Use Cases
- Read replica synchronization
- Cache warming (SQL → Elasticsearch)
- Backup synchronization
- Multi-region data sync
- Offline-first applications

## Best Practices

1. **Batch Size** - Use appropriate batch sizes for performance
2. **Error Handling** - Handle sync errors gracefully
3. **Monitoring** - Monitor sync performance and conflicts
4. **Scheduling** - Schedule syncs during low-traffic periods
5. **Conflict Strategy** - Choose appropriate conflict resolution
6. **Incremental Sync** - Sync only changed data when possible

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
