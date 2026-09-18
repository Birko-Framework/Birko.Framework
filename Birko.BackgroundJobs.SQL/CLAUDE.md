# Birko.BackgroundJobs.SQL

## Overview

SQL-based persistent job queue implementing `IJobQueue` from Birko.BackgroundJobs. Built on the Birko data layer and Birko.Data.SQL — uses `AsyncDataBaseBulkStore<DB, JobDescriptorModel>` for all database operations.

## Structure

```
Birko.BackgroundJobs.SQL/
├── Models/
│   └── JobDescriptorModel.cs   - AbstractModel with SQL attributes, ToDescriptor/FromDescriptor mapping
├── SqlJobQueue.cs              - IJobQueue<DB> using AsyncDataBaseBulkStore (enqueue, dequeue, complete, fail, cancel, purge)
├── SqlJobQueueSchema.cs        - Schema utilities (EnsureCreatedAsync, DropAsync via connector)
└── SqlJobLockProvider.cs       - Advisory lock provider using Birko.Data.SQL connector
```

## Dependencies

- Birko.BackgroundJobs (IJobQueue, JobDescriptor, JobStatus, RetryPolicy)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings, SqlSettings, OrderBy)
- Birko.Data.SQL (AsyncDataBaseBulkStore, AbstractConnector, SqlSettings, attributes, DataBase factory)

## Key Design Decisions

- **Generic `<DB>` parameter** — `SqlJobQueue<DB> where DB : AbstractConnector` works with any SQL provider (PostgreSQL, MSSql, MySQL, SQLite)
- **AbstractModel-based persistence** — `JobDescriptorModel` extends `AbstractModel` with `[Table]`/`[NamedField]`/`[PrimaryField]` attributes for automatic SQL mapping
- **Store-based CRUD** — All operations go through `AsyncDataBaseBulkStore<DB, JobDescriptorModel>` — no raw ADO.NET
- **Expression-based queries** — Dequeue uses lambda expressions (`j => j.Status == pending`) parsed by the SQL connector
- **SqlSettings for connection** — Uses `SqlSettings` (from Birko.Data.SQL.Stores) with `CommandTimeout`, `ConnectionTimeout`, and provider-specific overrides. Falls back to `PasswordSettings` for backward compatibility.
- **Store exposed publicly** — `SqlJobQueue.Store` property allows external transaction context (`SetTransactionContext`) and SqlUnitOfWork integration
- **Status as int** — `JobStatus` enum stored as int column for SQL compatibility; model uses `int Status` property with casts
- **Metadata as JSON** — `Dictionary<string, string>` serialized to a single TEXT column

## Maintenance

### README Updates
When changing the model, adding new operations, or changing SQL provider support, update README.md.

### CLAUDE.md Updates
When adding/removing files or changing architecture, update the structure tree and design decisions.

### Test Requirements
Tests should be created in `Birko.BackgroundJobs.SQL.Tests` covering:
- Enqueue/dequeue lifecycle with SQLite connector
- Concurrent dequeue (multiple workers)
- Retry and dead letter transitions
- Schema auto-creation via store.InitAsync
- Purge operations
- OrderBy sorting in dequeue
