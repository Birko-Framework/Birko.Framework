# Birko.BackgroundJobs.RavenDB

## Overview
RavenDB-based persistent job queue for Birko.BackgroundJobs. Uses `AsyncRavenDBStore` from Birko.Data.RavenDB.

## Project Location
`Birko.BackgroundJobs.RavenDB/`

## Components

### Models
- `RavenJobDescriptorModel` - Extends `AbstractModel`, convention-based mapping (no special attributes), maps to/from `JobDescriptor`

### Core
- `RavenDBJobQueue` - `IJobQueue` implementation using `AsyncRavenDBStore<RavenJobDescriptorModel>`
- `RavenDBJobQueueSchema` - Static utility for database creation/deletion

## Dependencies
- Birko.BackgroundJobs (IJobQueue, JobDescriptor, RetryPolicy)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (OrderBy)
- Birko.Data.RavenDB (AsyncRavenDBStore, Settings)
- RavenDB.Client

## Concurrency
Dequeue, Complete, Fail and Cancel are compare-and-sets through `AsyncRavenDBStore.UpdateIfAsync` (TASK-665): the
document is loaded by id, the condition (due / Processing / Pending | Scheduled) is checked on it, and it is saved with
optimistic concurrency. The candidate query is index-served and may be stale, so it is only a hint. The filtered
`UpdateAsync` this used before (TASK-158, and the `ClaimToken` re-read of CR-M021 before that) runs on RavenDB as that
same query followed by an unconditional write, and a cancel racing a claim let both win (measured). A store substituted
for tests must override `UpdateIfAsync`. Job handlers should still be idempotent: a reclaim after the claim timeout can
hand a slow job to a second worker.

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Settings type is `Birko.Data.RavenDB.Stores.Settings` (typed descendant of RemoteSettings with RequestTimeout)
- Store supports transactions via `SetTransactionContext(IAsyncDocumentSession)`
