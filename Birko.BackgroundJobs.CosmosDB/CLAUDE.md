# Birko.BackgroundJobs.CosmosDB

## Overview
Cosmos DB-based persistent job queue for Birko.BackgroundJobs. Uses `AsyncCosmosDBStore` from Birko.Data.CosmosDB.

## Project Location
`C:\Source\Birko.BackgroundJobs.CosmosDB\`

## Components

### Models
- `CosmosJobDescriptorModel` - Extends `AbstractModel`, maps to/from `JobDescriptor`

### Core
- `CosmosDBJobQueue` - `IJobQueue` implementation using `AsyncCosmosDBStore<CosmosJobDescriptorModel>`
- `CosmosDBJobQueueSchema` - Static utility for container creation/deletion

## Dependencies
- Birko.BackgroundJobs (IJobQueue, JobDescriptor, JobStatus)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (OrderBy)
- Birko.Data.CosmosDB (AsyncCosmosDBStore, Settings)
- Microsoft.Azure.Cosmos

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Settings type is `Birko.Data.CosmosDB.Stores.Settings` (typed descendant of RemoteSettings with PartitionKeyPath, RequestTimeout, AllowBulkExecution)
