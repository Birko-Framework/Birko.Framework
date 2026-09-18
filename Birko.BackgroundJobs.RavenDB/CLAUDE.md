# Birko.BackgroundJobs.RavenDB

## Overview
RavenDB-based persistent job queue for Birko.BackgroundJobs. Uses `AsyncRavenDBStore` from Birko.Data.RavenDB.

## Project Location
`C:\Source\Birko.BackgroundJobs.RavenDB\`

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
`DequeueAsync` claims a job with a conditional update guarded on the still-eligible status plus a
`ClaimToken` re-read verification (CR-M021). The verify resolves a single winner even under RavenDB
last-write-wins: whichever worker wrote last owns the token, and every worker re-reads the same final
token, so exactly one matches and the losers move to the next candidate. Job handlers should still be
idempotent as defence in depth.

## Maintenance
- Keep in sync with IJobQueue interface changes in Birko.BackgroundJobs
- Settings type is `Birko.Data.RavenDB.Stores.Settings` (typed descendant of RemoteSettings with RequestTimeout)
- Store supports transactions via `SetTransactionContext(IAsyncDocumentSession)`
