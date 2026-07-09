# Birko.BackgroundJobs.MongoDB

## Overview
MongoDB-based persistent job queue for Birko.BackgroundJobs. Uses `AsyncMongoDBStore` from Birko.Data.MongoDB.

## Project Location
`C:\Source\Birko.BackgroundJobs.MongoDB\`

## Components

### Models
- `MongoJobDescriptorModel` - Extends `AbstractModel`, uses `[BsonElement]` attributes, maps to/from `JobDescriptor`
  - `CollectionName` property for MongoDB collection targeting

### Core
- `MongoDBJobQueue` - `IJobQueue` implementation using `AsyncMongoDBStore<MongoJobDescriptorModel>`
- `MongoDBJobQueueSchema` - Static utility for collection creation/deletion

## Dependencies
- Birko.BackgroundJobs (IJobQueue, JobDescriptor, RetryPolicy)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (OrderBy)
- Birko.Data.MongoDB (AsyncMongoDBStore, Settings)
- MongoDB.Driver / MongoDB.Bson

## Concurrency
`DequeueAsync` claims a job with a conditional update guarded on the still-eligible status plus a
`ClaimToken` re-read verification (CR-M020). MongoDB applies the `$set` per document atomically, so
only one racing worker's filter matches after the first flips the status; the losers move on to the
next candidate. Job handlers should still be idempotent as defence in depth.

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Settings type is `Birko.Data.MongoDB.Stores.Settings`
- Store supports transactions via `SetTransactionContext(IClientSessionHandle)`
