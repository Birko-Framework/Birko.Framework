# Birko.BackgroundJobs.SQL

SQL-based persistent job queue for the Birko Background Jobs framework. Built on Birko.Data and Birko.Data.SQL for seamless integration with the framework's data access layer.

## Features

- **Persistent storage** — Jobs survive process restarts, stored via Birko.Data.SQL stores
- **Auto-schema creation** — Table created automatically by the SQL connector on first use
- **Any SQL provider** — Works with any `AbstractConnector` (PostgreSQL, MSSql, MySQL, SQLite)
- **Expression-based queries** — Uses Birko.Data lambda expressions for filtering
- **Transaction support** — Integrates with `SqlTransactionContext` for atomic operations
- **Retry with backoff** — Failed jobs are re-scheduled with configurable delay
- **Advisory locking** — Optional `SqlJobLockProvider` for cross-worker coordination

## Dependencies

- Birko.BackgroundJobs (core interfaces)
- Birko.Data (AbstractModel, stores, settings)
- Birko.Data.SQL (AsyncDataBaseBulkStore, connectors, attributes)

## Usage

### Basic Setup

```csharp
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.SQL;
using Birko.BackgroundJobs.Processing;
using Birko.Data.Stores;

// Connection settings (same as any Birko.Data.SQL store)
var settings = new PasswordSettings
{
    Location = "localhost",
    Name = "mydb",
    Password = "secret"
};

// Create SQL job queue with a specific connector
var queue = new SqlJobQueue<PostgreSqlConnector>(settings);

// Use with dispatcher
var dispatcher = new JobDispatcher(queue);
await dispatcher.EnqueueAsync<MyJob>();

// Use with processor
var executor = new JobExecutor(type => serviceProvider.GetRequiredService(type));
var processor = new BackgroundJobProcessor(queue, executor);
await processor.RunAsync(cancellationToken);
```

### Pre-configured Store

```csharp
// Use an existing store for advanced scenarios (e.g., shared connector)
var store = new AsyncDataBaseBulkStore<MSSqlConnector, JobDescriptorModel>();
store.SetSettings(settings);

var queue = new SqlJobQueue<MSSqlConnector>(store);
```

### Transaction Integration

```csharp
// Access the underlying store for transaction context
var uow = SqlUnitOfWork.FromStore(queue.Store);
await uow.BeginAsync();

// Operations within the same transaction
queue.Store.SetTransactionContext(uow.Context);
await queue.EnqueueAsync(new JobDescriptor { ... });

await uow.CommitAsync();
```

### Distributed Locking

```csharp
using var lockProvider = new SqlJobLockProvider<PostgreSqlConnector>(settings);

if (await lockProvider.TryAcquireAsync("my-queue", TimeSpan.FromSeconds(10)))
{
    await processor.RunAsync(cancellationToken);
    await lockProvider.ReleaseAsync("my-queue");
}
```

### Schema Management

```csharp
// Auto-create table (happens automatically on first use)
await SqlJobQueueSchema.EnsureCreatedAsync<PostgreSqlConnector>(settings);

// Drop table (WARNING: deletes all jobs)
await SqlJobQueueSchema.DropAsync<PostgreSqlConnector>(settings);
```

## API Reference

| Type | Description |
|------|-------------|
| `SqlJobQueue<DB>` | `IJobQueue` implementation using `AsyncDataBaseBulkStore<DB, JobDescriptorModel>` |
| `JobDescriptorModel` | `AbstractModel` with SQL attributes mapping to the jobs table |
| `SqlJobQueueSchema` | Schema creation/drop utilities |
| `SqlJobLockProvider<DB>` | Advisory lock provider using Birko.Data.SQL connector |

## Table Schema

The `__BackgroundJobs` table is defined via `[Table]` and `[NamedField]` attributes on `JobDescriptorModel`:

| Column | Type | Description |
|--------|------|-------------|
| Id | GUID (PK) | Job identifier (mapped from AbstractModel.Guid) |
| JobType | string | Assembly-qualified type name |
| InputType | string? | Input type name |
| SerializedInput | string? | JSON-serialized input |
| QueueName | string? | Queue name |
| Priority | int | Priority (higher = first) |
| MaxRetries | int | Max retry attempts |
| Status | int | JobStatus enum value |
| AttemptCount | int | Number of attempts |
| EnqueuedAt | DateTime | When enqueued |
| ScheduledAt | DateTime? | When eligible for processing |
| LastAttemptAt | DateTime? | Last attempt time |
| CompletedAt | DateTime? | Completion time |
| LastError | string? | Last failure message |
| Metadata | string? | JSON metadata |

## Related Projects

- **Birko.BackgroundJobs** — Core interfaces and in-memory implementation
- **Birko.BackgroundJobs.Redis** — Redis-based persistent job queue (planned)
- **Birko.Data.SQL** — SQL store infrastructure

## License

Part of the Birko Framework.
