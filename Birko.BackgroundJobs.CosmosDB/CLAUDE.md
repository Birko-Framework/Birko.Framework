# Birko.BackgroundJobs.CosmosDB

## Overview
Cosmos DB-based persistent job queue for Birko.BackgroundJobs. Uses `AsyncCosmosDBStore` from Birko.Data.CosmosDB.

## Project Location
`Birko.BackgroundJobs.CosmosDB/`

## Components

### Models
- `CosmosJobDescriptorModel` - Extends `AbstractModel`, maps to/from `JobDescriptor`

### Core
- `CosmosDBJobQueue` - `IJobQueue` implementation using `AsyncCosmosDBStore<CosmosJobDescriptorModel>`
- `CosmosDBJobQueueSchema` - Static utility for container creation/deletion

## Concurrency (TASK-661)
- Dequeue, Complete, Fail and Cancel are compare-and-sets through `AsyncCosmosDBStore.UpdateIfAsync` (the item's ETag):
  the condition (due / Processing / Pending | Scheduled) is checked on the item as stored, and a lost race re-reads it.
  Dequeue tries up to 8 candidates per round, 4 rounds. A store substituted for tests must override `UpdateIfAsync`.

## Dependencies
- Birko.BackgroundJobs (IJobQueue, JobDescriptor, JobStatus)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (OrderBy)
- Birko.Data.CosmosDB (AsyncCosmosDBStore, Settings)
- Microsoft.Azure.Cosmos

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Settings type is `Birko.Data.CosmosDB.Stores.Settings` (typed descendant of RemoteSettings with PartitionKeyPath, RequestTimeout, AllowBulkExecution)
