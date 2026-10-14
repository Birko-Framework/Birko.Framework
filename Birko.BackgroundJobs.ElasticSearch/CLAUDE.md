# Birko.BackgroundJobs.ElasticSearch

## Overview
Elasticsearch-based persistent job queue for Birko.BackgroundJobs. Uses `AsyncElasticSearchStore` from Birko.Data.ElasticSearch.

## Project Location
`Birko.BackgroundJobs.ElasticSearch/`

## Components

### Models
- `ElasticJobDescriptorModel` - Extends `AbstractModel`, uses NEST attributes (`[Keyword]`, `[Number]`, `[Date]`, `[Text]`), maps to/from `JobDescriptor`

### Core
- `ElasticSearchJobQueue` - `IJobQueue` implementation using `AsyncElasticSearchStore<ElasticJobDescriptorModel>`
- `ElasticSearchJobQueueSchema` - Static utility for index creation/deletion

## Dependencies
- Birko.BackgroundJobs (IJobQueue, JobDescriptor, RetryPolicy)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (OrderBy)
- Birko.Data.ElasticSearch (AsyncElasticSearchStore, Settings)
- NEST / Elasticsearch.Net

## Concurrency
`DequeueAsync` claims a job with a conditional update guarded on the still-eligible status plus a
`ClaimToken` re-read verification (CR-M016), so a worker that loses a race skips the row and tries
the next candidate. The re-read is a **realtime GET** (`AsyncElasticSearchStore.ReadRealtimeAsync`), never a
search: a search sees the claim only after the next refresh (~1 s), and TASK-616 measured what that did. Every
re-read was stale, the token never matched, and one dequeue claimed every eligible job, left them all in
Processing and returned none. The claim's `update_by_query` writes with the version it matched, so a job another
worker already claimed is a version conflict and does not land. Concurrent claims were not measured. Delivery is
still at-least-once (a worker can die after claiming), so **job handlers must be idempotent.**

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Model attributes must match Elasticsearch mapping expectations
- Settings type is `Birko.Data.ElasticSearch.Stores.Settings`
