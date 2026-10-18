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
Dequeue, Complete, Fail and Cancel are compare-and-sets through `AsyncElasticSearchStore.UpdateIfAsync` (TASK-667): a
**realtime GET**, the condition (due / Processing / Pending | Scheduled) checked on that document, and a write
conditioned on its `_seq_no` / `_primary_term`; a 409 re-reads. `GetAsync` is a realtime GET too. Nothing that decides
or reads back a single job may use a search: a search sees a write only after the next refresh (~1 s).
- TASK-616 measured that for the claim's read-back (every claim stranded).
- TASK-667 measured it for the transitions TASK-158 made `update_by_query` calls. A job completed or failed within a
  second of its claim was still indexed Pending, the Processing guard matched nothing and the outcome was dropped,
  and Cancel cancelled a running job.

The candidate search is only a hint. Delivery is still at-least-once (a reclaim after the claim timeout can hand a slow
job to a second worker), so **job handlers must be idempotent.** A store substituted for tests must override
`UpdateIfAsync`.

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Model attributes must match Elasticsearch mapping expectations
- Settings type is `Birko.Data.ElasticSearch.Stores.Settings`
