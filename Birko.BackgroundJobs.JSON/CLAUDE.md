# Birko.BackgroundJobs.JSON

## Overview
JSON file-based job queue for Birko.BackgroundJobs. Uses `AsyncJsonStore` from Birko.Data.JSON. Ideal for development, testing, and single-process deployments.

## Project Location
`C:\Source\Birko.BackgroundJobs.JSON\`

## Components

### Models
- `JsonJobDescriptorModel` - Extends `AbstractModel`, uses `[JsonPropertyName]` attributes, maps to/from `JobDescriptor`

### Core
- `JsonJobQueue` - `IJobQueue` implementation using `AsyncJsonStore<JsonJobDescriptorModel>`
- `JsonJobQueueSchema` - Static utility for file creation/deletion

## Dependencies
- Birko.BackgroundJobs (IJobQueue, JobDescriptor, RetryPolicy)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (OrderBy, Settings)
- Birko.Data.JSON (AsyncJsonStore)
- Birko.Serialization — ISerializer for job metadata serialization (optional, defaults to SystemJsonSerializer)
- System.Text.Json

## Concurrency
`DequeueAsync` serializes its read-claim-update with a `SemaphoreSlim` (CR-M018) so multiple worker
tasks **in the same process** cannot claim the same job — mirroring the reference `InMemoryJobQueue`.
The file store has no compare-and-swap, so **cross-process** concurrency is still unsupported by
design; use a database backend (SQL/Mongo/…) for multi-process workers.

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Settings type is `Birko.Data.Stores.Settings` (from Birko.Data.Stores, basic Location + Name)
- No external database dependencies — stores jobs as JSON file on disk
