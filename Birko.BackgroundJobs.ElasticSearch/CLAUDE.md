# Birko.BackgroundJobs.ElasticSearch

## Overview
Elasticsearch-based persistent job queue for Birko.BackgroundJobs. Uses `AsyncElasticSearchStore` from Birko.Data.ElasticSearch.

## Project Location
`C:\Source\Birko.BackgroundJobs.ElasticSearch\`

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
the next candidate. **Caveat:** Elasticsearch refreshes its index only ~once per second by default,
so the post-claim re-read can be briefly stale — this narrows but does not fully eliminate the
double-dispatch window. **Job handlers must be idempotent.**

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Model attributes must match Elasticsearch mapping expectations
- Settings type is `Birko.Data.ElasticSearch.Stores.Settings`
