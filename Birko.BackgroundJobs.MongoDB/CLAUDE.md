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
- Birko.Data (AbstractModel, OrderBy)
- Birko.Data.MongoDB (AsyncMongoDBStore, Settings)
- MongoDB.Driver / MongoDB.Bson

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Settings type is `Birko.Data.MongoDB.Stores.Settings`
- Store supports transactions via `SetTransactionContext(IClientSessionHandle)`
