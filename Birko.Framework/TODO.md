# Birko Framework TODO

This document tracks planned features, enhancements, and new projects for the Birko Framework.

## Architecture Principle

**When to create separate projects vs. co-locate in providers:**

### Separate projects (`Birko.Feature.Platform/`)
Use when the platform implementation is a **substantial subsystem** with its own models, settings, stores, or multiple files:
```
Birko.Data.Migrations/             - Core interfaces (IMigration, IMigrationRunner)
Birko.Data.Migrations.SQL/         - SqlMigrationRunner, SqlMigrationStore, SqlMigrationSettings (4 files)
Birko.Data.Sync/                   - Core sync engine (15+ files)
Birko.Data.Sync.Sql/              - SQL sync knowledge tracking
Birko.Data.Sync.Tenant/           - Tenant-aware sync provider (5 files)
```
Examples: Migrations, Sync, Caching, BackgroundJobs, Workflow, Storage, Messaging, MessageQueue

### Co-locate in provider projects (`Birko.Data.SQL/Feature/`)
Use when the platform implementation is a **thin adapter** (1-2 files) tightly coupled to the provider:
```
Birko.Data.Patterns/               - Core interfaces (IUnitOfWork, PagedResult, ISpecification)
Birko.Data.SQL/UnitOfWork/         - SqlUnitOfWork (single file, uses AbstractConnectorBase)
Birko.Data.SQL/Paging/             - SqlPagedRepository (single file, uses OFFSET/FETCH)
Birko.Data.MongoDB/UnitOfWork/     - MongoDbUnitOfWork (single file, uses IClientSessionHandle)
```
Examples: UnitOfWork, Paging, platform-specific Specifications

### Decision rule
- **1-2 files, no own models/settings, tightly coupled to provider** → inside provider
- **3+ files, own models/settings, self-contained subsystem** → separate project

---

## Priority Legend

- **High** - Immediate value, low complexity
- **Medium** - Good value, moderate complexity
- **Low** - Nice to have, or high complexity

---

## Symbio Alignment

Symbio (`C:\Source\Symbio`) is the primary consumer of Birko Framework (33 Birko projects referenced). Phases are ordered to reflect Symbio's real needs:

### Already integrated (Symbio uses today)
- Birko.Data.* (SQL, MongoDB, TimescaleDB, RavenDB, Elasticsearch) — full data access layer
- Birko.Data.Patterns (UoW interfaces) — Symbio has stub SqlUnitOfWork, needs full repo participation
- Birko.Data.Tenant — multi-tenancy via X-Tenant-Id header
- Birko.Data.Migrations — module-aware migration runner (Symbio extends with topological sorting)
- Birko.Data.EventSourcing — outbox pattern (planned)
- Birko.Security + Birko.Security.Jwt + Birko.Security.AspNetCore — password hashing, AES-256-GCM, JWT tokens, ASP.NET Core integration
- Birko.Communication.SSE + WebSocket — real-time notifications
- **Birko.Caching** ✅ — ICache registered directly (MemoryCache singleton), replaced Symbio ICacheService
- **Birko.Validation** ✅ — ValidationFilter<T>, fluent validators in Building + IoT modules
- **Birko.BackgroundJobs** ✅ — PollDeviceJob, ProcessTelemetryJob, AddBackgroundJobs() DI extension
- **Birko.MessageQueue + MQTT + InMemory** ✅ — MqttDeviceAdapter, MqttIngestionService, MessageQueueExtensions
- **Birko.EventBus** ✅ — In-process bus, distributed via MessageQueue, outbox pattern, event sourcing bridge, deduplication, pipeline behaviors
- **Birko.Storage** ✅ — IFileStorage, LocalFileStorage, path sanitization, metadata, tenant prefix
- **Birko.Messaging** ✅ — IEmailSender, SmtpEmailSender, StringTemplateEngine, ISmsSender/IPushSender interfaces

### Symbio is actively blocked on
- Nothing currently — all blocking dependencies resolved

### Recently unblocked
- **Birko.Messaging** ✅ (Phase 7) — email invoices, reservation confirmations, SMS notifications
- **Birko.Storage** ✅ (Phase 6) — product images, invoice PDFs, camera snapshots

### Recommended next for Symbio (priority order)

1. **Birko.Health** (High) — Symbio runs 5 database backends (SQL, MongoDB, TimescaleDB, RavenDB, ES) + MQTT + Redis + background jobs. Aggregated health endpoint is essential for production monitoring. Small project, quick win.
2. **Birko.Caching.Hybrid** (High) — Symbio uses MemoryCache singleton today. Multi-node deployments need L1 memory + L2 Redis with distributed invalidation for cache consistency across instances in a multi-tenant setup.
3. **Birko.Storage.AzureBlob** (High) — LocalFileStorage works for dev but product images, invoice PDFs, and camera snapshots need cloud storage for production scaling.
4. **Birko.Data.Aggregates** (Medium) — Symbio uses 5 data stores. Aggregate mapper simplifies keeping denormalized ES search indices in sync with relational SQL data, especially for m:n relations (e.g., products ↔ categories).
5. **Birko.Messaging — Razor templates** (Medium) — Symbio sends email invoices and reservation confirmations via StringTemplateEngine. Razor templates enable proper HTML email layout with loops, conditionals, and partial views.
6. **Birko.Workflow** (Medium) — Reservations, order tracking, and device lifecycle likely have ad-hoc state machines today. Formalizing with a workflow engine improves correctness and auditability.
7. **Birko.MessageQueue.Redis** (Low) — Redis infra already exists. Redis Streams would provide a persistent non-IoT queue without deploying a separate broker (RabbitMQ/Kafka).
8. **Birko.CQRS** (Low) — Natural next step given EventBus + EventSourcing are integrated. Separates read/write models for complex modules.

### Lower priority for Symbio
- **Birko.Time** — `DateTimeOffset` covers most needs unless business calendar/working hours become a requirement
- **Birko.Serialization** — `System.Text.Json` works fine, only needed if MessagePack/Protobuf performance is required
- **Birko.Localization** — Only if Symbio needs multi-language UI
- **Birko.MessageQueue.Kafka/RabbitMQ** — MQTT + InMemory covers IoT workloads, only needed at higher scale

### Symbio-specific features (not in Birko scope)
- Module discovery/registration system (IModule, ModuleRegistrar, dependency graph)
- Unified real-time notifier (SSE + WebSocket combined, tenant-aware)
- Time-series store abstraction (generic over TimescaleDB)
- Module-aware migration runner (topological dependency sorting)
- SQL dialect abstraction (PostgreSqlDialect, MsSqlDialect)

---

## Architectural Notes: Unit of Work Pattern

### Why Unit of Work for the Birko data layer?

The Birko data layer is **not an ORM** - it's a data access framework with explicit Store/Repository patterns. Each operation (Create, Update, Delete) runs as a single transaction:

```csharp
// Current behavior - each call is its own transaction
store.Create(customer);    // Transaction 1: BEGIN INSERT COMMIT
store.Create(order);       // Transaction 2: BEGIN INSERT COMMIT
store.Update(product);     // Transaction 3: BEGIN UPDATE COMMIT
```

**Problem:** Multiple operations cannot be atomic. If one fails, others are already committed.

**Unit of Work Solution:** Batch multiple operations into one transaction:

```csharp
// With Unit of Work
using var uow = new SqlUnitOfWork(store.Connector);
uow.Add(customer);
uow.Add(order);
uow.Update(product);
await uow.CommitAsync();   // Single transaction: BEGIN INSERT INSERT UPDATE COMMIT
```

### Birko Data Layer Transactions vs Unit of Work

| Aspect | Birko Stores (Current) | Unit of Work |
|--------|---------------------|--------------|
| **Transaction Scope** | Per operation | Per workflow |
| **Change Tracking** | None | Tracks Add/Update/Delete |
| **Commit Control** | Automatic (implicit) | Explicit (CommitAsync) |
| **Multi-Op Atomicity** | ❌ No | ✅ Yes |
| **Overhead** | Minimal | Tracking overhead |
| **Use Case** | Simple CRUD | Complex business transactions |

### Implementation Approaches

Two valid approaches discussed:

#### Approach 1: Track & Batch (Recommended)
Track entities, execute all in one transaction on CommitAsync.

**Pros:**
- Single transaction
- True atomicity
- Works with all platforms

**Cons:**
- More code
- Need to generate SQL/commands manually

#### Approach 2: Repository Wrapper
Wrap existing repositories to share a transaction context.

**Pros:**
- Works with existing code
- Simpler implementation
- Flexible

**Cons:**
- Doesn't reduce DB round trips
- Still uses underlying store operations

### Platform Transaction Capabilities

| Platform | Transaction Support | UoW Approach |
|----------|-------------------|--------------|
| **SQL (MSSql, PostgreSQL, MySQL, SQLite)** | Full ACID via `DbTransaction` | Track & batch SQL commands |
| **TimescaleDB** | PostgreSQL transactions | Same as SQL |
| **MongoDB** | Sessions with `WithTransaction` (v4.0+) | Use `IClientSessionHandle` |
| **RavenDB** | `DocumentSession.SaveChanges()` (built-in UoW) | Wrap existing session |
| **Elasticsearch** | No ACID (eventually consistent) | Bulk API batching only |
| **InfluxDB** | Limited (batch writes) | Batch point collection |

**Key Insight:** Each platform requires its own implementation. RavenDB already has UoW built-in. Elasticsearch cannot provide true transactions - only best-effort batching.

### Performance Considerations

**Birko stores alone can be faster than ORMs:**
- No change tracking overhead
- No LINQ expression tree parsing
- Direct SQL execution
- No lazy loading proxy generation

**Unit of Work adds overhead:**
- Entity tracking in memory
- Deferred execution
- Transaction management

**Conclusion:** Use Birko stores directly for simple operations. Use Unit of Work only when multi-operation atomicity is required.

### Project Structure Decision

Unit of Work interfaces live in **Birko.Data.Patterns** (core). Platform implementations are **co-located in their provider projects** (not separate projects) because each UoW is a single thin adapter file:

```
Birko.Data.Patterns/                   - IUnitOfWork, IUnitOfWork<TContext>, exceptions
Birko.Data.SQL/UnitOfWork/             - SqlUnitOfWork (uses AbstractConnectorBase)
Birko.Data.MongoDB/UnitOfWork/         - MongoDbUnitOfWork (uses IClientSessionHandle)
Birko.Data.RavenDB/UnitOfWork/         - RavenDbUnitOfWork (uses IDocumentSession)
Birko.Data.ElasticSearch/UnitOfWork/   - ElasticSearchUnitOfWork (bulk API batching)
Birko.Data.InfluxDB/UnitOfWork/        - InfluxDbUnitOfWork (batch point writes)
```

This avoids 6 extra projects for what amounts to 6 single-file adapters. Compare with Migrations/Sync which justify separate projects because they have 3-5+ files with own models and settings.

---

## Phase 1: Data Access Patterns (High Priority)

> **Symbio integration:** Symbio references Birko.Data.Patterns and has a stub `SqlUnitOfWork` where repositories don't participate in the transaction. Paging (`GetPaged`) is implemented in Symbio's `IRepository<T>` but not backed by Birko's `IPagedRepository` yet.

### Birko.Data.Patterns
**Status:** ✅ Complete | **Priority:** High

Core interfaces and platform-agnostic patterns for data access.
Location: `C:\Source\Birko.Data.Patterns\`

```
Birko.Data.Patterns/
├── UnitOfWork/
│   ├── IUnitOfWork.cs                     ✅ IUnitOfWork + IUnitOfWork<TContext>
│   └── UnitOfWorkException.cs             ✅ Base + NoActiveTransaction + TransactionAlreadyActive
├── Models/
│   ├── ISoftDeletable.cs                  ✅ DeletedAt, IsDeleted
│   ├── IAuditable.cs                      ✅ CreatedBy, UpdatedBy
│   └── IAuditContext.cs                   ✅ CurrentUserId provider
├── Decorators/
│   ├── SoftDeleteStoreWrapper.cs          ✅ Sync single store
│   ├── SoftDeleteBulkStoreWrapper.cs      ✅ Sync bulk store
│   ├── AsyncSoftDeleteStoreWrapper.cs     ✅ Async single store
│   ├── AsyncSoftDeleteBulkStoreWrapper.cs ✅ Async bulk store
│   ├── SoftDeleteFilter.cs               ✅ Filter expression
│   ├── AuditStoreWrapper.cs              ✅ Sync single store
│   ├── AuditBulkStoreWrapper.cs          ✅ Sync bulk store
│   ├── AsyncAuditStoreWrapper.cs         ✅ Async single store
│   └── AsyncAuditBulkStoreWrapper.cs     ✅ Async bulk store
├── Paging/
│   ├── PagedResult.cs                     ✅ Page, PageSize, TotalCount, TotalPages, HasNext/Previous
│   ├── IPagedRepository.cs               ✅ IPagedRepository<T> + IAsyncPagedRepository<T>
│   ├── PagedRepositoryWrapper.cs         ✅ Sync wrapper over IBulkRepository<T>
│   └── AsyncPagedRepositoryWrapper.cs    ✅ Async wrapper over IAsyncBulkRepository<T>
├── Specification/
│   ├── ISpecification.cs                  ✅ IsSatisfiedBy + ToExpression
│   ├── Specification.cs                   ✅ Base class with And/Or/Not + operators
│   ├── AndSpecification.cs                ✅ Logical AND composition
│   ├── OrSpecification.cs                 ✅ Logical OR composition
│   └── NotSpecification.cs               ✅ Logical NOT
├── Validation/
│   └── (Moved to Birko.Validation)        ✅ See Phase 3
└── Concurrency/
    ├── IVersioned.cs                      ✅ Version property for optimistic concurrency
    ├── ConcurrentUpdateException.cs       ✅ EntityType, EntityId, ExpectedVersion
    ├── VersionedStoreWrapper.cs           ✅ Sync IStore<T> wrapper
    └── AsyncVersionedStoreWrapper.cs      ✅ Async IAsyncStore<T> wrapper
```

**Dependencies:** Birko.Data.Core, Birko.Data.Stores

---

### Platform UoW — co-located in provider projects
**Status:** ✅ Implemented | **Priority:** High

UoW implementations are single-file adapters, co-located in their provider projects (not separate projects).

| Provider | File | Context Type | Status |
|----------|------|-------------|--------|
| **SQL** | `Birko.Data.SQL/UnitOfWork/SqlUnitOfWork.cs` | `SqlTransactionContext` (DbConnection+DbTransaction) | ✅ |
| **MongoDB** | `Birko.Data.MongoDB/UnitOfWork/MongoDbUnitOfWork.cs` | `IClientSessionHandle` | ✅ |
| **RavenDB** | `Birko.Data.RavenDB/UnitOfWork/RavenDbUnitOfWork.cs` | `IAsyncDocumentSession` | ✅ |
| **ElasticSearch** | `Birko.Data.ElasticSearch/UnitOfWork/ElasticSearchUnitOfWork.cs` | `BulkOperationContext` (no ACID) | ✅ |
| **InfluxDB** | `Birko.Data.InfluxDB/UnitOfWork/InfluxDbUnitOfWork.cs` | `BatchPointContext` (batch writes) | ✅ |
| **TimescaleDB** | Inherits SQL | Same as SQL | ✅ (no separate file needed) |

All implementations include `FromStore<T>()` factory method. All registered in `.projitems`.

---

### Platform Paging — generic wrappers (no platform-specific code needed)
**Status:** ✅ Implemented | **Priority:** High

All stores already implement `Read(filter, orderBy, limit, offset)` and `Count(filter)` natively. Paging wrappers in `Birko.Data.Patterns/Paging/` combine these two calls into `PagedResult<T>` — no platform-specific implementations required.

| Wrapper | Description |
|---------|-------------|
| `PagedRepositoryWrapper<T>` | Wraps any `IBulkRepository<T>` for sync paging |
| `AsyncPagedRepositoryWrapper<T>` | Wraps any `IAsyncBulkRepository<T>` for async paging (parallel Read+Count) |

---

## Phase 2: Caching (High Priority)

> **Symbio integration:** Symbio has a placeholder `ICacheService` with `InMemoryCacheService` (wraps MS `MemoryCache`). Needs to swap to `Birko.Caching.ICache` for richer API and Redis support.

### Birko.Caching
**Status:** ✅ Implemented | **Priority:** High

Core caching interfaces and in-memory implementation.
Location: `C:\Source\Birko.Caching\`

```
Birko.Caching/
├── Core/
│   ├── ICache.cs                          ✅ Get/Set/Remove/Exists/GetOrSet/RemoveByPrefix/Clear async
│   ├── CacheEntryOptions.cs               ✅ AbsoluteExpiration, SlidingExpiration, CachePriority
│   └── CacheResult.cs                     ✅ Hit(value) / Miss() struct
├── Memory/
│   ├── MemoryCache.cs                     ✅ ConcurrentDictionary, background eviction, stampede protection
│   └── MemoryCacheEntry.cs                ✅ Internal entry with expiration tracking
└── Serialization/
    └── CacheSerializer.cs                 ✅ System.Text.Json serialize/deserialize
```

**Dependencies:** None (core only)

---

### Birko.Caching.Redis
**Status:** ✅ Implemented | **Priority:** High

Redis caching implementation.
Location: `C:\Source\Birko.Caching.Redis\`

```
Birko.Caching.Redis/
├── RedisCache.cs                          ✅ ICache over StackExchange.Redis, sliding via metadata hash
├── RedisCacheOptions.cs                   ✅ ConnectionString, InstanceName, DefaultExpiration, Database
└── RedisConnectionManager.cs              ✅ Lazy<ConnectionMultiplexer> singleton
```

**Dependencies:** Birko.Caching, StackExchange.Redis (NuGet added by consuming project)

---

### Birko.Caching.Hybrid
**Status:** Planned | **Priority:** Medium

L1 memory + L2 distributed cache.

```
Birko.Caching.Hybrid/
├── HybridCache.cs                         - Two-tier caching
└── HybridCacheOptions.cs
```

**Dependencies:** Birko.Caching, Birko.Caching.Redis (or other distributed)

---

### Birko.Caching.NCache
**Status:** Planned | **Priority:** Low

NCache implementation.

```
Birko.Caching.NCache/
└── NCache.cs
```

**Dependencies:** Birko.Caching, Alachisoft.NCache.Client

---

## Phase 3: Validation (High Priority)

> **Symbio integration:** Symbio has zero input validation on any endpoint. Needs to define validators for all modules and wire up `ValidatingStoreWrapper` decorators.

### Birko.Validation
**Status:** ✅ Implemented | **Priority:** High

Fluent validation - platform-agnostic, no separate platform projects needed.
Location: `C:\Source\Birko.Validation\`

```
Birko.Validation/
├── Core/
│   ├── IValidator.cs                      ✅ IValidator<T> with Validate/ValidateAsync
│   ├── IValidationRule.cs                 ✅ IValidationRule with IsValid(value, context)
│   ├── ValidationResult.cs                ✅ IsValid, Errors, AddError, Merge, ToDictionary
│   ├── ValidationContext.cs               ✅ Instance, InstanceType, PropertyName, Items
│   └── ValidationException.cs             ✅ Thrown by store wrappers on failure
├── Rules/
│   ├── RequiredRule.cs                    ✅ Not null/empty/whitespace/empty-guid
│   ├── EmailRule.cs                       ✅ Regex email format (GeneratedRegex)
│   ├── RangeRule.cs                       ✅ IComparable min/max
│   ├── LengthRule.cs                      ✅ String min/max length
│   ├── RegexRule.cs                       ✅ Custom regex pattern
│   └── CustomRule.cs                      ✅ Func predicate + strongly-typed CustomRule<T>
├── Fluent/
│   ├── AbstractValidator.cs               ✅ Base class with RuleFor<TProp>()
│   ├── RuleBuilder.cs                     ✅ Chaining: Required/Email/MaxLength/Range/Must/In/NotEqual/etc.
│   └── PropertyRule.cs                    ✅ Holds rules for a single property, extracts value via expression
└── Integration/
    ├── ValidatingStoreWrapper.cs          ✅ Sync IStore<T> wrapper
    ├── AsyncValidatingStoreWrapper.cs     ✅ Async IAsyncStore<T> wrapper
    └── AsyncValidatingBulkStoreWrapper.cs ✅ Async IAsyncBulkStore<T> wrapper
```

**Dependencies:** Birko.Data.Core, Birko.Data.Stores (for store integration)

---

## Phase 4: Background Jobs (High Priority)

> **Symbio impact:** IoT module's `DevicePollingService` and `TelemetryProcessor` use raw `IHostedService` — no retry, no persistence, no distributed locking. This is a production blocker for reliable IoT data processing.

### Birko.BackgroundJobs
**Status:** ✅ Implemented | **Priority:** High

Core interfaces and in-memory implementation.
Location: `C:\Source\Birko.BackgroundJobs\`

```
Birko.BackgroundJobs/
├── Core/
│   ├── IJob.cs                            ✅ IJob + IJob<TInput>
│   ├── IJobQueue.cs                       ✅ Enqueue/Dequeue/Complete/Fail/Cancel/Purge
│   ├── IJobExecutor.cs                    ✅ Resolve and execute job instances
│   ├── JobDescriptor.cs                   ✅ Full job description (type, input, status, retries)
│   ├── JobResult.cs                       ✅ Succeeded/Failed with duration
│   ├── JobStatus.cs                       ✅ Pending/Scheduled/Processing/Completed/Failed/Dead/Cancelled
│   ├── JobContext.cs                      ✅ Runtime context (JobId, AttemptNumber, Metadata)
│   ├── JobQueueOptions.cs                 ✅ Concurrency, polling, timeout, retention config
│   └── RetryPolicy.cs                    ✅ Exponential backoff retry configuration
├── Serialization/
│   ├── IJobSerializer.cs                  ✅ Serialize/deserialize job inputs
│   └── JsonJobSerializer.cs              ✅ System.Text.Json implementation
└── Processing/
    ├── BackgroundJobProcessor.cs          ✅ Concurrent polling processor with semaphore
    ├── InMemoryJobQueue.cs               ✅ ConcurrentDictionary-based IJobQueue
    ├── JobDispatcher.cs                  ✅ High-level fluent API
    ├── JobExecutor.cs                    ✅ DI factory + reflection invocation
    └── RecurringJobScheduler.cs          ✅ Interval-based recurring jobs
```

**Dependencies:** None (core only)

---

### Birko.BackgroundJobs.SQL
**Status:** ✅ Implemented | **Priority:** High

SQL-based persistent job queue storage.
Location: `C:\Source\Birko.BackgroundJobs.SQL\`

```
Birko.BackgroundJobs.SQL/
├── Models/
│   └── JobDescriptorModel.cs              ✅ AbstractModel with SQL attributes + ToDescriptor/FromDescriptor
├── SqlJobQueue.cs                         ✅ IJobQueue<DB> using AsyncDataBaseBulkStore
├── SqlJobQueueSchema.cs                   ✅ Schema utilities via connector (EnsureCreated/Drop)
└── SqlJobLockProvider.cs                  ✅ Advisory locks using Birko.Data.SQL connector
```

**Dependencies:** Birko.BackgroundJobs, Birko.Data.Core, Birko.Data.Stores, Birko.Data.SQL

---

### Birko.BackgroundJobs.Redis
**Status:** ✅ Implemented | **Priority:** High

Redis-based persistent job queue.
Location: `C:\Source\Birko.BackgroundJobs.Redis\`

```
Birko.BackgroundJobs.Redis/
├── RedisJobQueue.cs                       ✅ IJobQueue using Redis hashes + sorted sets, Lua atomic dequeue
├── RedisJobQueueOptions.cs                ✅ ConnectionString, KeyPrefix, Database
├── RedisConnectionManager.cs              ✅ Lazy<ConnectionMultiplexer> singleton
└── RedisJobLockProvider.cs                ✅ SET NX + Lua safe release (Redlock single-instance)
```

**Dependencies:** Birko.BackgroundJobs, StackExchange.Redis (NuGet added by consuming project)

---

## Phase 5: Message Queue — MQTT Early Extract (High Priority)

> **Symbio impact:** IoT module currently uses MQTTnet directly for device communication (sensors, gateways, telemetry). Standardizing on Birko.MessageQueue abstractions enables consistent patterns across all messaging and decouples IoT from a specific MQTT library.

### Birko.MessageQueue (Core)
**Status:** ✅ Implemented | **Priority:** High

Core interfaces for asynchronous messaging between services (pub/sub, point-to-point).

```
Birko.MessageQueue/
├── Core/
│   ├── IMessageQueue.cs                    - Core queue interface (combined producer/consumer)
│   ├── IMessageProducer.cs                 - Send/publish messages
│   ├── IMessageConsumer.cs                 - Subscribe/receive messages
│   ├── IMessageHandler.cs                  - Typed message handler
│   ├── ISubscription.cs                    - Active subscription handle (disposable)
│   ├── QueueMessage.cs                     - Message wrapper (Id, Body, Headers, Priority, TTL, Delay)
│   ├── MessageHeaders.cs                   - Metadata (CorrelationId, ReplyTo, ContentType, GroupId, Custom)
│   ├── MessageContext.cs                   - Runtime context (Message, Destination, DeliveryCount)
│   ├── ConsumerOptions.cs                  - Subscription config (AckMode, PrefetchCount, GroupId)
│   ├── MessageAckMode.cs                   - AutoAck, ManualAck
│   └── MessageFingerprint.cs              - SHA256 content fingerprinting for deduplication
├── Patterns/
│   ├── IPublisher.cs                       - Pub/Sub pattern
│   ├── ISubscriber.cs                      - Typed lambda subscription
│   ├── ISender.cs                          - Point-to-point pattern
│   └── IReceiver.cs                        - Pull-based receive
├── Serialization/
│   ├── IMessageSerializer.cs               - Serialize/deserialize messages
│   ├── JsonMessageSerializer.cs            - JSON (default, System.Text.Json)
│   └── EncryptingMessageSerializer.cs      - Decorator with pluggable encrypt/decrypt
├── Retry/
│   ├── RetryPolicy.cs                      - Exponential backoff (MaxRetries, BaseDelay, MaxDelay)
│   └── DeadLetterOptions.cs                - DLQ routing (suffix-based or explicit destination)
└── Transactions/
    └── ITransactionalProducer.cs           - Begin/Commit/Rollback transactional send
```

**Dependencies:** None (core only)

---

### Birko.MessageQueue.MQTT
**Status:** ✅ Implemented | **Priority:** High

MQTT implementation via MQTTnet — extracted early from Phase 8 due to Symbio IoT dependency.

```
Birko.MessageQueue.MQTT/
├── MqttMessageQueue.cs                    - IMessageQueue implementation (auto-reconnect)
├── MqttProducer.cs                        - Publish to topics (QoS, retain per-message)
├── MqttConsumer.cs                        - Subscribe with wildcard matching
├── MqttSubscription.cs                    - ISubscription implementation
├── MqttTopic.cs                           - Topic validation and wildcard matching (+, #)
├── MqttSettings.cs                        - RemoteSettings subclass (host, TLS, CleanSession, reconnect, LWT)
├── MqttQualityOfService.cs                - QoS 0, 1, 2
└── MqttLastWill.cs                        - LWT configuration
```

**Features:**
- QoS levels (0: At most once, 1: At least once, 2: Exactly once)
- Persistent sessions (CleanSession = false)
- Topic wildcards (+ single level, # multi level)
- Retained messages (via retain flag on publish)
- Last Will and Testament
- TLS/SSL support (server + mutual TLS with client certificates)
- Automatic reconnect with configurable delay and max attempts

**Use Cases:**
- IoT device communication
- Sensor networks
- Real-time telemetry
- Home automation
- Edge computing

**Dependencies:** Birko.MessageQueue, MQTTnet

---

### Birko.MessageQueue.InMemory
**Status:** ✅ Implemented | **Priority:** High

In-memory channel-based implementation for testing/development. Needed alongside MQTT so Symbio can unit-test without a broker.

```
Birko.MessageQueue.InMemory/
├── InMemoryMessageQueue.cs                - IMessageQueue facade (configurable capacity)
├── InMemoryProducer.cs                    - IMessageProducer (delayed delivery support)
├── InMemoryConsumer.cs                    - IMessageConsumer (auto/manual ack)
├── InMemoryChannel.cs                     - Per-destination BoundedChannel with dispatch loop
├── InMemorySubscription.cs                - ISubscription implementation
└── InMemoryMessageQueueOptions.cs         - Channel capacity configuration
```

**Features:**
- No external dependencies
- Bounded channels with backpressure (configurable capacity, default 1000)
- Per-destination topic routing
- Pub/sub fanout to multiple subscribers
- Fast (in-process) for testing and development

**Dependencies:** Birko.MessageQueue, System.Threading.Channels

---

## Phase 6: Storage (Medium Priority)

> **Symbio impact:** Needed for product images (Eshop), invoice PDFs (Invoicing), camera snapshots (IoT), and guest documents (Hotel). No file storage abstraction exists in Symbio today.

### Birko.Storage
**Status:** ✅ Implemented | **Priority:** Medium

Core interfaces and local filesystem implementation.
Location: `C:\Source\Birko.Storage\`

```
Birko.Storage/
├── Core/
│   ├── IFileStorage.cs                    ✅ Upload/Download/Delete/Exists/GetReference/List/Copy/Move
│   ├── IPresignedUrlStorage.cs            ✅ Optional cloud capability (GetDownloadUrl/GetUploadUrl)
│   ├── FileReference.cs                   ✅ Path, FileName, ContentType, Size, CreatedAt, ETag, Metadata
│   ├── StorageResult.cs                   ✅ Found/NotFound readonly struct
│   ├── StorageSettings.cs                 ✅ Extends Birko.Data.Stores.Settings (Location, Name, PathPrefix)
│   ├── StorageOptions.cs                  ✅ MaxFileSize, AllowedContentTypes, OverwriteExisting, Metadata
│   ├── PresignedUrlOptions.cs             ✅ Expiry, ContentDisposition, ContentType
│   └── StorageException.cs               ✅ FileAlreadyExists, FileTooLarge, ContentTypeNotAllowed, InvalidPath
├── Local/
│   └── LocalFileStorage.cs               ✅ Filesystem impl, path sanitization, .meta.json companion files
└── Extensions/
    └── FileStorageExtensions.cs           ✅ UploadBytes, UploadFile, DownloadBytes, DownloadToFile
```

**Dependencies:** Birko.Data.Stores (for Settings/ISettings base classes)

---

### Birko.Storage.AzureBlob
**Status:** Planned | **Priority:** Medium

Azure Blob Storage provider.

```
Birko.Storage.AzureBlob/
├── AzureBlobStorage.cs                   - IFileStorage + IPresignedUrlStorage implementation
├── AzureBlobSettings.cs                  - Extends RemoteSettings (ConnectionString, ContainerName)
└── AzureBlobPresignedUrlProvider.cs      - SAS token-based presigned URLs
```

**Dependencies:** Birko.Storage, Azure.Storage.Blobs

---

### Birko.Storage.Aws
**Status:** Planned | **Priority:** Medium

AWS S3.

```
Birko.Storage.Aws/
└── S3Storage.cs
```

**Dependencies:** Birko.Storage, AWSSDK.S3

---

### Birko.Storage.Google
**Status:** Planned | **Priority:** Low

Google Cloud Storage.

```
Birko.Storage.Google/
└── GoogleCloudStorage.cs
```

**Dependencies:** Birko.Storage, Google.Cloud.Storage.V1

---

### Birko.Storage.Minio
**Status:** Planned | **Priority:** Low

MinIO (S3-compatible self-hosted).

```
Birko.Storage.Minio/
└── MinioStorage.cs
```

**Dependencies:** Birko.Storage, Minio

---

## Phase 7: Messaging (Medium Priority)

> **Symbio impact:** Needed for invoice emails (Invoicing), reservation confirmations (Hotel), order notifications (Eshop), alarm alerts (IoT). Symbio has no email/SMS implementation today.

### Birko.Messaging
**Status:** ✅ Implemented | **Priority:** Medium

Core interfaces, SMTP email, push/SMS interfaces, and string template engine.
Location: `C:\Source\Birko.Messaging\`

```
Birko.Messaging/
├── Core/
│   ├── IMessage.cs                          ✅ Base interface (Id, Recipients, Body, ScheduledAt, Metadata)
│   ├── IMessageSender.cs                    ✅ Generic sender (SendAsync, SendBatchAsync)
│   ├── IMessageTemplate.cs                  ✅ Template definition (Name, Subject, BodyTemplate, IsHtml)
│   ├── MessageAddress.cs                    ✅ Recipient (Value + DisplayName, case-insensitive equality)
│   ├── MessageAttachment.cs                 ✅ File attachment (FileName, ContentType, Stream, IsInline)
│   ├── MessageResult.cs                     ✅ Succeeded/Failed with MessageId and Timestamp
│   └── MessagingException.cs               ✅ MessageDeliveryException, InvalidRecipientException, TemplateRenderException
├── Email/
│   ├── IEmailSender.cs                      ✅ Extends IMessageSender<EmailMessage> with convenience overload
│   ├── EmailMessage.cs                      ✅ From, To, Cc, Bcc, ReplyTo, Subject, IsHtml, Attachments, Priority
│   ├── EmailSettings.cs                     ✅ Extends RemoteSettings (Host/Port/UserName/Password + Timeout, DefaultFrom)
│   └── SmtpEmailSender.cs                   ✅ System.Net.Mail SMTP implementation, IDisposable
├── Sms/
│   ├── ISmsSender.cs                        ✅ Interface only (implementations in provider projects)
│   └── SmsMessage.cs                        ✅ IMessage with From (phone number)
├── Push/
│   ├── IPushSender.cs                       ✅ Interface only (implementations in provider projects)
│   └── PushMessage.cs                       ✅ IMessage with Title, ImageUrl, ClickAction, Badge, Sound
└── Templates/
    ├── ITemplateEngine.cs                   ✅ RenderAsync(template, model) + RenderAsync(IMessageTemplate, model)
    └── StringTemplateEngine.cs              ✅ {{Property.SubProperty}} replacement via reflection, GeneratedRegex
```

**Dependencies:** Birko.Data.Core, Birko.Data.Stores (RemoteSettings), System.Net.Mail (for SMTP)

---

### Birko.Messaging.SendGrid
**Status:** Planned | **Priority:** Medium

SendGrid email provider.

```
Birko.Messaging.SendGrid/
└── SendGridEmailSender.cs
```

**Dependencies:** Birko.Messaging, SendGrid

---

### Birko.Messaging.Mailgun
**Status:** Planned | **Priority:** Low

Mailgun email provider.

```
Birko.Messaging.Mailgun/
└── MailgunEmailSender.cs
```

**Dependencies:** Birko.Messaging, Mailgun

---

### Birko.Messaging.Twilio
**Status:** Planned | **Priority:** Medium

Twilio SMS provider.

```
Birko.Messaging.Twilio/
└── TwilioSmsSender.cs
```

**Dependencies:** Birko.Messaging, Twilio

---

### Birko.Messaging.Razor
**Status:** ✅ Implemented (2026-03-17) | **Priority:** Medium

Razor template engine for rich HTML email and message rendering. Replaces StringTemplateEngine for complex templates with conditionals, loops, and layouts.
Location: `C:\Source\Birko.Messaging.Razor\`

```
Birko.Messaging.Razor/
├── RazorTemplateEngine.cs                ✅ ITemplateEngine implementation using RazorLight
├── RazorTemplateOptions.cs               ✅ Template base path, caching, encoding, default namespaces
└── RazorFileTemplateProvider.cs          ✅ Load .cshtml templates from disk with caching and traversal protection
```

**Features:**
- Full Razor syntax (conditionals, loops, partials, layouts)
- Strongly-typed models (`@model OrderConfirmation`)
- Template caching (compiled templates cached for reuse)
- File-based templates with directory traversal protection
- IMessageTemplate support (file lookup by name, fallback to inline BodyTemplate)
- Configurable encoding and default namespaces

**Dependencies:** Birko.Messaging, RazorLight (NuGet added by consuming project)

---

### Birko.Messaging.Firebase
**Status:** Planned | **Priority:** Low

Firebase Cloud Messaging (push notifications).

```
Birko.Messaging.Firebase/
└── FcmPushSender.cs
```

**Dependencies:** Birko.Messaging, FirebaseAdmin

---

### Birko.Messaging.Apple
**Status:** Planned | **Priority:** Low

Apple Push Notification Service.

```
Birko.Messaging.Apple/
└── ApplePushSender.cs
```

**Dependencies:** Birko.Messaging, PushSharp or APNs SDK

---

## Phase 8: Message Queue — Remaining Providers (Medium Priority)

> **Note:** Core MessageQueue interfaces and MQTT were extracted to Phase 5 due to Symbio IoT needs. This phase covers the remaining distributed queue providers.

### Birko.MessageQueue.RabbitMQ
**Status:** Planned | **Priority:** Medium

RabbitMQ implementation (AMQP 0-9-1).

```
Birko.MessageQueue.RabbitMQ/
├── RabbitMQMessageQueue.cs                - Connection + channel management
├── RabbitMQProducer.cs                    - Publish messages
├── RabbitMQConsumer.cs                    - Consume messages
├── RabbitMQExchange.cs                    - Exchange management
├── RabbitMQQueue.cs                       - Queue management (declare, bind)
├── RabbitMQOptions.cs                     - Connection settings
└── RabbitMQExtensions.cs                  - Convention-based routing
```

**Features:**
- Exchange types: Direct, Topic, Fanout, Headers
- Queue declaration with TTL, max length, dead-letter
- Consumer with prefetch/QoS control
- Publisher confirms
- Automatic reconnection

**Dependencies:** Birko.MessageQueue, RabbitMQ.Client

---

### Birko.MessageQueue.Kafka
**Status:** Planned | **Priority:** Medium

Apache Kafka implementation.

```
Birko.MessageQueue.Kafka/
├── KafkaMessageQueue.cs                   - Producer/Consumer management
├── KafkaProducer.cs                       - Publish to topics
├── KafkaConsumer.cs                       - Consume from topics
├── KafkaTopic.cs                          - Topic management
├── KafkaConsumerGroup.cs                  - Consumer group coordination
├── KafkaOptions.cs                        - Bootstrap servers, config
└── KafkaExtensions.cs                     - SerDes, partitioning
```

**Features:**
- Topic partitioning
- Consumer group coordination
- Offset management (earliest, latest, committed)
- Message keys for partitioning
- Exactly-once semantics support

**Dependencies:** Birko.MessageQueue, Confluent.Kafka

---

### Birko.MessageQueue.Azure
**Status:** Planned | **Priority:** Low

Azure Service Bus implementation.

```
Birko.MessageQueue.Azure/
├── AzureServiceBusQueue.cs                - Queue client
├── AzureServiceBusTopic.cs                - Topic/Subscription client
├── AzureMessageProducer.cs                - Send messages
├── AzureMessageConsumer.cs                - Receive messages (with Sessions)
├── AzureServiceBusOptions.cs              - Connection string
└── AzureDeadLetterHandler.cs              - DLQ handling
```

**Features:**
- Queues and Topics/Subscriptions
- Message sessions
- Scheduled messages
- Peek-Lock pattern
- Dead-letter queues

**Dependencies:** Birko.MessageQueue, Azure.Messaging.ServiceBus

---

### Birko.MessageQueue.Aws
**Status:** Planned | **Priority:** Low

AWS SQS implementation.

```
Birko.MessageQueue.Aws/
├── SqsMessageQueue.cs                     - SQS client wrapper
├── SqsProducer.cs                         - Send messages
├── SqsConsumer.cs                         - Receive/delete messages
├── SqsQueue.cs                            - Queue management
├── SqsOptions.cs                          - Credentials, region
└── SqsExtensions.cs                       - Message attributes
```

**Features:**
- Standard and FIFO queues
- Message batching (up to 10 messages)
- Long polling
- Delayed messages
- Dead-letter queues

**Dependencies:** Birko.MessageQueue, AWSSDK.SQS

---

### Birko.MessageQueue.Redis
**Status:** Planned | **Priority:** Low

Redis Streams implementation (lightweight pub/sub).

```
Birko.MessageQueue.Redis/
├── RedisStreamQueue.cs                    - Redis Streams client
├── RedisProducer.cs                       - XADD
├── RedisConsumer.cs                       - XREAD, XREADGROUP
├── RedisConsumerGroup.cs                  - Consumer group management
├── RedisStreamOptions.cs                  - Connection settings
└── RedisExtensions.cs                     - Stream operations
```

**Features:**
- Consumer groups
- Message pending (ACK with XACK)
- Message trimming (XTRIM)
- Consumer lag monitoring

**Dependencies:** Birko.MessageQueue, StackExchange.Redis

---

### Birko.MessageQueue.MassTransit
**Status:** Planned | **Priority:** Low

MassTransit wrapper (existing .NET messaging framework).

```
Birko.MessageQueue.MassTransit/
├── MassTransitAdapter.cs                  - Adapt MassTransit to Birko.MessageQueue
├── MassTransitConsumerRegistrar.cs        - Register consumers
└── MassTransitSagaExtensions.cs           - Saga integration
```

**Dependencies:** Birko.MessageQueue, MassTransit, MassTransit.RabbitMQ (or other transport)

---

## Phase 9: Event Bus (High Priority)

> **Symbio impact:** Symbio has `InMemoryEventBus` + 10 integration events across Building and IoT modules. The bus is in-process only, no persistence, no retry, no DLQ. Symbio needs: (1) outbox pattern for transactional publish, (2) distributed delivery via MessageQueue providers, (3) deduplication, (4) tenant propagation. `Birko.EventBus` provides the bridge between strongly-typed Symbio events and transport-agnostic Birko.MessageQueue.
>
> **Architecture:**
> ```
> ┌─────────────────────────────────────────────────────────┐
> │  MODULE CODE                                             │
> │  await eventBus.PublishAsync(new OrderPlaced(...))        │
> │  class OrderPlacedHandler : IEventHandler<OrderPlaced>   │
> ├─────────────────────────────────────────────────────────┤
> │  Birko.EventBus (this phase)                             │
> │  IEventBus → Pipeline → Outbox/Direct → Dispatch         │
> ├─────────────────────────────────────────────────────────┤
> │  Birko.MessageQueue (Phase 5 — done)                     │
> │  InMemory · MQTT · RabbitMQ · Kafka (Phase 8)            │
> └─────────────────────────────────────────────────────────┘
> ```
>
> **Key difference from Symbio's current IEventBus:**
> - Symbio `IEventBus`: strongly-typed generics `<T>`, in-process only, no persistence
> - Birko `IMessageQueue`: string destinations, JSON payloads, transport-agnostic
> - Birko `IEventBus`: bridges both — strongly-typed API, pluggable transport backend

### Birko.EventBus
**Status:** ✅ Done | **Priority:** High

Core event bus interfaces, in-process implementation, pipeline behaviors, and DI integration.

```
Birko.EventBus/
├── Core/
│   ├── IEvent.cs                             - Marker interface for events
│   ├── IEventHandler.cs                      - IEventHandler<TEvent> : HandleAsync(event, context, ct)
│   ├── IEventBus.cs                          - PublishAsync<TEvent>, Subscribe<TEvent>
│   ├── IEventSubscription.cs                 - Subscription handle (Dispose to unsubscribe)
│   ├── EventBase.cs                          - Base record (Id, Timestamp, CorrelationId, Source)
│   └── EventContext.cs                       - Handler context (EventId, Source, CorrelationId,
│                                               TenantId, DeliveryCount, Metadata)
├── Local/
│   ├── InProcessEventBus.cs                  - In-memory event bus (ConcurrentDictionary<Type, handlers>)
│   │                                           DI handler resolution, async dispatch, error isolation
│   └── InProcessEventBusOptions.cs           - MaxConcurrency, ErrorHandling (stop/continue/dlq)
├── Pipeline/
│   ├── IEventPipelineBehavior.cs             - Middleware: before/after handler (logging, validation, retry)
│   └── EventPipeline.cs                      - Ordered pipeline executor (Russian doll pattern)
├── Routing/
│   ├── ITopicConvention.cs                   - Event type → topic name strategy
│   ├── DefaultTopicConvention.cs             - "{source}.{event-name-kebab}" e.g. "building.space-created"
│   └── AttributeTopicConvention.cs           - [Topic("custom.topic")] attribute on event class
├── Enrichment/
│   ├── IEventEnricher.cs                     - Pre-publish enrichment (add headers, metadata)
│   ├── TenantEventEnricher.cs                - Injects TenantId from ITenantContext into event headers
│   └── CorrelationEventEnricher.cs           - Propagates/generates CorrelationId
├── Deduplication/
│   ├── IDeduplicationStore.cs                - Check/record processed event IDs
│   ├── InMemoryDeduplicationStore.cs         - ConcurrentDictionary with TTL expiry
│   └── DeduplicationBehavior.cs              - IEventPipelineBehavior that skips duplicate events
└── Extensions/
    └── EventBusServiceCollectionExtensions.cs
        - AddEventBus() — in-process bus + DI handler scan
        - AddEventBus<TBus>() — custom bus implementation
        - AddEventHandler<TEvent, THandler>() — explicit registration
        - AddEventPipelineBehavior<T>() — register pipeline behavior
        - AddEventEnricher<T>() — register enricher
```

**Features:**
- Strongly-typed event publishing and handling
- In-process event bus for modular monolith (single process)
- Pipeline behaviors (logging, retry, validation, dedup — ordered middleware chain)
- Multiple handlers per event type
- Async event dispatch (fire-and-forget or await-all, configurable)
- Event correlation (CorrelationId propagation across handlers)
- Tenant propagation (TenantId injected from context, available to handlers)
- Handler ordering via priority
- DI integration (handlers auto-discovered from IServiceProvider)
- Configurable topic conventions (default or attribute-based)
- Deduplication (MessageFingerprint or EventId-based)
- Error isolation (handler failure doesn't cascade to other handlers)

**Dependencies:** None (core interfaces only)

**Symbio migration path:**
1. Symbio `IEventBus` interface aligns with `Birko.EventBus.IEventBus`
2. Symbio `InMemoryEventBus` replaced by `Birko.EventBus.InProcessEventBus`
3. Symbio `IntegrationEvent` base record → extends `Birko.EventBus.EventBase`
4. Symbio `IEventHandler<T>` → `Birko.EventBus.IEventHandler<T>` (add EventContext param)
5. Pipeline behaviors replace Symbio's planned EventDispatcher (TODO 4.4)
6. TenantEventEnricher replaces manual tenant header injection

---

### Birko.EventBus.MessageQueue
**Status:** ✅ Done | **Priority:** High

Distributed event bus backed by Birko.MessageQueue providers. Enables cross-process event delivery via any transport (InMemory for tests, MQTT for IoT, RabbitMQ/Kafka for production).

```
Birko.EventBus.MessageQueue/
├── DistributedEventBus.cs                    - IEventBus over IMessageQueue
│                                               Serializes events → QueueMessage, publishes to topic
│                                               Subscribes to topics, deserializes, dispatches to handlers
├── DistributedEventBusOptions.cs             - TopicConvention, IMessageSerializer, RetryPolicy,
│                                               DeadLetterOptions, SubscriptionMode (auto/manual)
├── EventEnvelope.cs                          - Wraps IEvent with metadata for transport
│                                               (EventType assembly-qualified name, Headers, TenantId)
├── AutoSubscriber.cs                         - Scans DI for IEventHandler<T> registrations
│                                               Creates IMessageConsumer subscriptions for each event type
│                                               Runs on startup (IHostedService)
└── DistributedEventBusHostedService.cs       - IHostedService — starts AutoSubscriber, manages lifecycle
```

**Features:**
- Publish events across processes/services via any MessageQueue provider
- Automatic event serialization/deserialization (IMessageSerializer — JSON default, encrypted optional)
- Topic naming via ITopicConvention (configurable, default: `{source}.{event-name-kebab}`)
- Auto-subscription: scans DI for `IEventHandler<T>`, creates subscriptions on startup
- Dead-letter handling for failed events (configurable DLQ suffix/destination)
- Retry with exponential backoff (reuses Birko.MessageQueue.RetryPolicy)
- Event replay support (when backed by Kafka or similar — FromBeginning option)
- Deduplication via MessageFingerprint on consumer side
- Consumer groups for load balancing (via ConsumerOptions.GroupId)

**Transport matrix:**
| Transport | Use Case | Persistence | Ordering |
|-----------|----------|-------------|----------|
| InMemory | Tests, dev, modular monolith | ❌ | Per-destination |
| MQTT | IoT events, edge → cloud | Broker-dependent | Per-topic |
| RabbitMQ | General distributed events | ✅ | Per-queue |
| Kafka | High-throughput, event replay | ✅ | Per-partition |
| Redis Streams | Lightweight distributed | TTL-based | Per-stream |

**Dependencies:** Birko.EventBus, Birko.MessageQueue

---

### Birko.EventBus.Outbox
**Status:** ✅ Done | **Priority:** High

Transactional outbox pattern — events are written to an outbox table in the same DB transaction as the business data, then published asynchronously by a background processor. Guarantees at-least-once delivery.

```
Birko.EventBus.Outbox/
├── Core/
│   ├── IOutboxStore.cs                       - CRUD for outbox entries (pending, published, failed)
│   ├── OutboxEntry.cs                        - EventId, EventType, Payload (JSON), TenantId,
│   │                                           CreatedAt, PublishedAt?, Attempts, Status
│   ├── OutboxStatus.cs                       - Pending, Publishing, Published, Failed
│   └── OutboxOptions.cs                      - BatchSize, PollingInterval, MaxAttempts, RetentionPeriod
├── Publishing/
│   ├── OutboxEventBus.cs                     - IEventBus decorator — writes events to outbox
│   │                                           instead of publishing directly. Same DB transaction
│   │                                           as the repository Save/Create that triggered the event.
│   └── OutboxProcessor.cs                    - Background loop: poll outbox → publish via IEventBus
│                                               → mark Published or increment Attempts → DLQ if exhausted
├── Stores/
│   ├── SqlOutboxStore.cs                     - IOutboxStore over Birko.Data.SQL (outbox_entries table)
│   └── InMemoryOutboxStore.cs                - IOutboxStore for tests (ConcurrentDictionary)
├── Hosting/
│   └── OutboxProcessorHostedService.cs       - IHostedService bridge for OutboxProcessor
└── Extensions/
    └── OutboxServiceCollectionExtensions.cs
        - AddOutbox<TStore>() — register outbox store + processor
        - AddOutboxEventBus() — wrap existing IEventBus with outbox decorator
```

**Flow:**
```
Module Code:
  await repository.CreateAsync(order);        // 1. Save business entity
  await eventBus.PublishAsync(new OrderPlaced(...));  // 2. OutboxEventBus writes to outbox table
  await unitOfWork.CommitAsync();             // 3. Both saved in same transaction

OutboxProcessor (background):
  Poll outbox_entries WHERE Status = Pending  // 4. Find unsent events
  Publish via inner IEventBus (distributed)   // 5. Send to MessageQueue
  Update Status = Published                   // 6. Mark as done
```

**Guarantees:**
- **At-least-once delivery** — events survive process crashes (persisted in DB)
- **Transactional consistency** — event stored in same transaction as business data
- **Idempotency** — consumers use DeduplicationBehavior (from Birko.EventBus) to handle duplicates
- **Retry** — failed publishes re-attempted up to MaxAttempts, then status = Failed
- **Cleanup** — published/failed entries purged after RetentionPeriod

**Dependencies:** Birko.EventBus, Birko.Data.Stores (IStore for outbox table), Birko.Data.Patterns (IUnitOfWork)

**Symbio migration path:**
- Replaces Symbio's planned OutboxProcessor (TODO 4.5)
- OutboxEventBus decorates the distributed bus — modules don't know about the outbox
- SqlOutboxStore uses Birko.Data.SQL for the outbox table

---

### Birko.EventBus.EventSourcing
**Status:** ✅ Done | **Priority:** Low

Integration between EventBus and EventSourcing for domain event replay and projections.

```
Birko.EventBus.EventSourcing/
├── EventStoreEventBus.cs                     - Publishes stored events to bus after append
├── EventReplayService.cs                     - Replay events from store through bus
└── ProjectionHandler.cs                      - Base class for event-sourced projections
```

**Features:**
- Automatic event publishing after event store append
- Event replay from event store through event bus
- Projection rebuilding via replayed events

**Dependencies:** Birko.EventBus, Birko.Data.EventSourcing

---

## Phase 10: Telemetry (Medium Priority)

> **Symbio impact:** Symbio has basic middleware (CorrelationIdMiddleware, RequestLoggingMiddleware) which is functional. Not a blocker but would benefit from structured logging and metrics for production monitoring.
>
> **Design Consideration (2026-03-15):** The original plan below (custom ILog/IMetrics/ITracer) is **over-engineered**. .NET 8+ already provides all three pillars natively:
> - `ILogger<T>` (Microsoft.Extensions.Logging) — structured logging. Symbio already uses it everywhere.
> - `System.Diagnostics.Metrics` — Counter, Histogram, Gauge, Meter (built-in, no NuGet needed).
> - `System.Diagnostics.Activity` / `ActivitySource` — distributed tracing (built-in, W3C TraceContext).
> - OpenTelemetry .NET SDK hooks directly into all three — no custom abstraction needed.
>
> Creating custom interfaces would **fight the platform**: every .NET library already emits telemetry via the built-in APIs, and custom wrappers would need adapters for everything.
>
> **Recommended alternative — thin Birko-specific instrumentation layer:**
> - **Telemetry conventions** — standard meter/activity source names, common dimensions (tenant, module, operation type)
> - **Store/Repository instrumentation** — auto-instrument CRUD operations with duration histograms, operation counters, error rates (decorator pattern like SoftDeleteStoreWrapper)
> - **Middleware helpers** — correlation ID propagation into Activity.Current baggage (Symbio does the header part, but not the tracing part)
> - **DI setup helpers** — `AddBirkoTelemetry()` that wires up OpenTelemetry with sensible defaults for all Birko stores/repos
> - **No custom ILog/IMetrics/ITracer** — use the .NET built-ins directly
>
> This would be a much smaller project (~10 files vs ~15) with zero API surface friction.

### Birko.Telemetry
**Status:** ✅ Implemented (2026-03-15) | **Priority:** Medium

Thin instrumentation layer over .NET built-in APIs. Store wrappers with metrics/tracing, correlation ID middleware, fluent extensions. See `C:\Source\Birko.Telemetry\README.md`.

~~Core interfaces and console/basic implementations.~~ (Original plan replaced with thin layer approach.)

```
Birko.Telemetry/
├── Core/
│   ├── ILog.cs                            - Logging interface
│   ├── LogLevel.cs
│   ├── IMetrics.cs                        - Metrics interface
│   ├── ITracer.cs                         - Tracing interface
│   └── LogContext.cs
├── Logging/
│   ├── StructuredLogger.cs                - Base implementation
│   ├── LogFormatter.cs
│   └── LogScope.cs
├── Metrics/
│   ├── Counter.cs
│   ├── Gauge.cs
│   ├── Histogram.cs
│   └── Meter.cs
├── Tracing/
│   ├── Tracer.cs                          - System.Diagnostics.Activity wrapper
│   ├── ActivitySpan.cs
│   └── TraceContext.cs
└── Console/
    └── ConsoleExporter.cs                 - Console output
```

**Dependencies:** None (core only)

---

### Birko.Telemetry.OpenTelemetry
**Status:** ✅ Implemented (2026-03-16) | **Priority:** Medium

OpenTelemetry exporters.

```
Birko.Telemetry.OpenTelemetry/
├── OpenTelemetryExporter.cs
├── OpenTelemetryMetricsExporter.cs
└── OpenTelemetryTracer.cs
```

**Dependencies:** Birko.Telemetry, OpenTelemetry, OpenTelemetry.Exporter.*

---

### Birko.Telemetry.Prometheus
**Status:** Planned | **Priority:** Low

Prometheus metrics exporter.

```
Birko.Telemetry.Prometheus/
└── PrometheusMetricsExporter.cs
```

**Dependencies:** Birko.Telemetry, Prometheus.Client

---

### Birko.Telemetry.Seq
**Status:** Planned | **Priority:** Low

Seq log exporter.

```
Birko.Telemetry.Seq/
└── SeqLogExporter.cs
```

**Dependencies:** Birko.Telemetry, Seq.Client

---

### Birko.Telemetry.Grafana
**Status:** Planned | **Priority:** Low

Grafana dashboard provisioning and LGTM stack integration helpers (Loki, Grafana, Tempo, Mimir).

```
Birko.Telemetry.Grafana/
├── GrafanaDashboardProvider.cs           - JSON dashboard provisioning for Birko store metrics
├── LokiLogExporter.cs                    - Push logs to Grafana Loki
└── GrafanaAnnotations.cs                 - Create Grafana annotations from events/deploys
```

**Dependencies:** Birko.Telemetry, Grafana HTTP API

---

## Phase 11: Security Extensions (✅ Complete)

> **Note:** All security projects are now implemented. Core Birko.Security, Birko.Security.Jwt, and Birko.Security.AspNetCore were already integrated into Symbio. BCrypt, Vault, and AzureKeyVault were added 2026-03-15.

### Birko.Security
**Status:** ✅ Implemented | **Priority:** Done

Core security interfaces, built-in implementations, static token authentication, and secret provider interface.
Location: `C:\Source\Birko.Security\`

```
Birko.Security/
├── Core/
│   ├── IPasswordHasher.cs                 ✅ Hash/Verify interface
│   ├── IEncryptionProvider.cs             ✅ Encrypt/Decrypt byte[] and string
│   ├── ITokenProvider.cs                  ✅ GenerateToken/ValidateToken + TokenResult/TokenOptions
│   └── ISecretProvider.cs               ✅ GetSecret/SetSecret/DeleteSecret/ListSecrets + SecretResult
├── Authentication/                        ✅ Moved from Birko.Communication.Authentication
│   ├── AuthenticationService.cs           ✅ Static token validation + IP binding
│   ├── AuthenticationConfiguration.cs     ✅ Enabled, Tokens[], TokenBindings[]
│   └── TokenBinding.cs                    ✅ Token + AllowedIps
├── Authorization/
│   └── IRoleProvider.cs                   ✅ IRoleProvider, IPermissionChecker, AuthorizationContext
├── Hashing/
│   └── Pbkdf2PasswordHasher.cs            ✅ PBKDF2-SHA512, 600k iterations, self-contained format
└── Encryption/
    └── AesEncryptionProvider.cs           ✅ AES-256-GCM, nonce+tag embedded
```

---

### Birko.Security.Jwt
**Status:** ✅ Implemented | **Priority:** Done

```
Birko.Security.Jwt/
└── JwtTokenProvider.cs                    ✅ ITokenProvider via System.IdentityModel.Tokens.Jwt
```

---

### Birko.Security.AspNetCore
**Status:** ✅ Implemented | **Priority:** Done

ASP.NET Core integration for Birko.Security — JWT Bearer authentication, current user resolution, permission checking, and multi-tenant middleware.
Location: `C:\Source\Birko.Security.AspNetCore\`

```
Birko.Security.AspNetCore/
├── User/
│   ├── ICurrentUser.cs                    ✅ Authenticated user interface (UserId, Email, TenantId, Roles, Permissions)
│   ├── ClaimMappingOptions.cs             ✅ JWT claim-to-property mapping configuration
│   └── ClaimsCurrentUser.cs               ✅ HttpContext-based ICurrentUser from JWT claims
├── Authentication/
│   ├── JwtClaimNames.cs                   ✅ Standard claim name constants (sub, email, tenant_id, role, permission)
│   ├── JwtAuthenticationOptions.cs        ✅ JWT Bearer configuration (Secret, Issuer, Audience, Expiration, ClockSkew)
│   ├── JwtBearerExtensions.cs             ✅ AddBirkoJwtBearer() DI extension
│   └── TokenServiceAdapter.cs             ✅ ITokenProvider wrapper with TokenRequest/TokenValidationInfo records
├── Authorization/
│   ├── ClaimsPermissionChecker.cs         ✅ IPermissionChecker from JWT claims (wildcard superadmin support)
│   └── PermissionEndpointFilter.cs        ✅ Minimal API RequirePermission() endpoint filter
├── Tenant/
│   ├── ITenantResolver.cs                 ✅ Interface + TenantInfo record
│   ├── HeaderTenantResolver.cs            ✅ X-Tenant-Id / X-Tenant-Name header resolution
│   ├── SubdomainTenantResolver.cs         ✅ Subdomain-based tenant with async lookup
│   ├── TenantContextAdapter.cs            ✅ Adapts Birko.Data.Tenant ITenantContext for scoped DI
│   └── TenantMiddleware.cs                ✅ Request-scoped tenant resolution middleware
└── Extensions/
    └── SecurityServiceExtensions.cs       ✅ AddBirkoSecurity() one-line DI (JWT + User + Permissions + Tenant)
```

**Dependencies:** Birko.Security, Birko.Security.Jwt, Birko.Data.Tenant, Microsoft.AspNetCore

---

### Birko.Security.BCrypt
**Status:** ✅ Implemented (2026-03-15) | **Priority:** Done

Pure C# BCrypt password hashing (no external NuGet). Configurable work factor (4–31), NeedsRehash support, standard `$2a$` format.
Location: `C:\Source\Birko.Security.BCrypt\`

```
Birko.Security.BCrypt/
└── Hashing/
    └── BCryptPasswordHasher.cs    ✅ IPasswordHasher, Blowfish, EksBlowfish, BCrypt-Base64
```

**Dependencies:** Birko.Security (IPasswordHasher interface only — no BCrypt.Net needed)

---

### Birko.Security.Vault
**Status:** ✅ Implemented (2026-03-15) | **Priority:** Done

HashiCorp Vault secret provider — uses Vault HTTP API directly, no VaultSharp dependency.
VaultSettings extends PasswordSettings (Location=Address, Password=Token, Name=MountPath).
Location: `C:\Source\Birko.Security.Vault\`

```
Birko.Security.Vault/
├── VaultSettings.cs              ✅ Extends PasswordSettings
└── VaultSecretProvider.cs        ✅ ISecretProvider, KV v1/v2, HTTP API, IsHealthyAsync
```

**Dependencies:** Birko.Security (ISecretProvider), Birko.Data.Stores (PasswordSettings)

---

### Birko.Security.AzureKeyVault
**Status:** ✅ Implemented (2026-03-15) | **Priority:** Done

Azure Key Vault secret provider — uses Key Vault REST API with OAuth2 client credentials, no Azure SDK dependency.
AzureKeyVaultSettings extends RemoteSettings (Location=VaultUri, UserName=ClientId, Password=ClientSecret, Name=TenantId).
Location: `C:\Source\Birko.Security.AzureKeyVault\`

```
Birko.Security.AzureKeyVault/
├── AzureKeyVaultSettings.cs          ✅ Extends RemoteSettings
└── AzureKeyVaultSecretProvider.cs    ✅ ISecretProvider, OAuth2 token caching, REST API v7.4
```

**Dependencies:** Birko.Security (ISecretProvider), Birko.Data.Stores (RemoteSettings)

---

## Phase 12: Workflow (Low Priority)

> **Symbio impact:** Future need for hotel reservations, production order tracking, order status workflows. Not urgent today.

### Birko.Workflow
**Status:** Planned | **Priority:** Low

State machine engine - platform-agnostic core.

```
Birko.Workflow/
├── Core/
│   ├── IWorkflow.cs
│   ├── IState.cs
│   ├── ITransition.cs
│   ├── ITransitionGuard.cs
│   ├── StateChangeEvent.cs
│   └── WorkflowContext.cs
├── Definition/
│   ├── WorkflowBuilder.cs                 - Fluent builder
│   ├── StateDefinition.cs
│   ├── TransitionDefinition.cs
│   └── WorkflowDefinition.cs
├── Execution/
│   ├── WorkflowEngine.cs                  - Execute workflows
│   ├── TransitionGuard.cs                 - Guard conditions
│   └── StateAction.cs                     - Entry/exit actions
└── Visualization/
    └── WorkflowDiagramGenerator.cs        - Generate diagrams
```

**Dependencies:** None (core only)

---

### Birko.Workflow.SQL
**Status:** Planned | **Priority:** Low

SQL workflow persistence.

```
Birko.Workflow.SQL/
├── SqlWorkflowStore.cs
└── SqlWorkflowSchema.cs
```

**Dependencies:** Birko.Workflow, Birko.Data.SQL

---

### Birko.Workflow.MongoDB
**Status:** Planned | **Priority:** Low

MongoDB workflow persistence.

```
Birko.Workflow.MongoDB/
└── MongoWorkflowStore.cs
```

**Dependencies:** Birko.Workflow, Birko.Data.MongoDB

---

## Phase 13: Additional Projects (Low Priority)

### Birko.Time
**Status:** Planned | **Priority:** Low

Time utilities - no platform-specific implementations needed.

```
Birko.Time/
├── Core/
│   ├── IDateTimeProvider.cs
│   ├── ITimeZoneConverter.cs
│   └── IBusinessCalendar.cs
├── Calendars/
│   ├── BusinessCalendar.cs
│   ├── Holiday.cs
│   ├── WorkingHours.cs
│   └── HolidayCalendar.cs
└── Providers/
    ├── SystemDateTimeProvider.cs
    └── TestDateTimeProvider.cs            - For testing
```

**Dependencies:** None

---

### Birko.Health
**Status:** Planned | **Priority:** Low

Health checks - separate projects per platform.

```
Birko.Health/
├── Core/
│   ├── IHealthCheck.cs
│   ├── HealthCheckResult.cs
│   └── HealthStatus.cs
└── System/
    ├── DiskSpaceHealthCheck.cs            - Built-in
    └── MemoryHealthCheck.cs               - Built-in
```

```
Birko.Health.Data/
├── SqlHealthCheck.cs
└── MongoDbHealthCheck.cs
```

```
Birko.Health.Redis/
└── RedisHealthCheck.cs
```

---

### Birko.Serialization
**Status:** Planned | **Priority:** Low

Serialization - separate projects per format.

```
Birko.Serialization/
├── Core/
│   ├── ISerializer.cs
│   └── SerializationFormat.cs
└── Json/
    └── SystemJsonSerializer.cs            - Built-in (System.Text.Json)
```

```
Birko.Serialization.Newtonsoft/
└── NewtonsoftJsonSerializer.cs
```

```
Birko.Serialization.MessagePack/
└── MessagePackSerializer.cs
```

```
Birko.Serialization.Protobuf/
└── ProtobufSerializer.cs
```

---

### Birko.Localization
**Status:** Planned | **Priority:** Low

Translations - separate projects per storage.

```
Birko.Localization/
├── Core/
│   ├── ILocalizer.cs
│   ├── ITranslationProvider.cs
│   └── CultureInfo.cs
├── Json/
│   └── JsonTranslationProvider.cs         - JSON files (built-in)
└── Resx/
    └── ResxTranslationProvider.cs         - RESX files (built-in)
```

```
Birko.Localization.Data/
└── DatabaseTranslationProvider.cs
```

---

### Birko.CQRS
**Status:** Planned | **Priority:** Low

Command Query Responsibility Segregation - platform-agnostic.

```
Birko.CQRS/
├── Core/
│   ├── ICommand.cs
│   ├── IQuery.cs
│   ├── ICommandHandler.cs
│   ├── IQueryHandler.cs
│   └── IRequestHandler.cs
├── Pipeline/
│   ├── ICommandPipeline.cs
│   ├── IQueryPipeline.cs
│   └── PipelineBehavior.cs
└── Mediator/
    └── Mediator.cs                        - Simple mediator
```

**Dependencies:** None

---

### Birko.Data.Processors `[Affiliate dependency]`
**Status:** ✅ Implemented (2026-03-16) | **Priority:** Medium

Generic stream processor framework inspired by Affiliate.Import. Provides event-driven, composable processors for XML, CSV, HTTP download, and ZIP extraction with decorator composition pattern.
Location: `C:\Source\Birko.Data.Processors\`

```
Birko.Data.Processors/                    (.shproj)
├── Core/
│   ├── IProcessor.cs                     ✅ IProcessor (Process/ProcessAsync), IStreamProcessor (+stream overloads)
│   ├── AbstractProcessor.cs              ✅ Generic base <T> with new() constraint, ILogger, sync+async event delegates
│   └── ProcessorException.cs             ✅ ProcessorException, ProcessorDownloadException, ProcessorParseException
├── Formats/
│   ├── XmlProcessor.cs                   ✅ XmlReader-based parser, virtual ProcessStream/Async + ProcessNode
│   └── CsvProcessor.cs                   ✅ CSV row/column parser, virtual ProcessStream/Async, uses Birko.Helpers.CsvParser
└── Transport/
    ├── HttpProcessor.cs                  ✅ HTTP download decorator, file cleanup, HttpClient injection
    └── ZipProcessor.cs                   ✅ ZIP extraction decorator, configurable EntryIndex, file cleanup
```

**Key design patterns:**
- **Generic `<T>` with `new()` constraint** — AOT-friendly, works with any model type
- **Decorator composition** — `HttpProcessor<ZipProcessor<XmlProcessor<T>, T>, T>` chains download→extract→parse
- **Event-driven** — `OnItemProcessed`, `OnElementStart/Value/End`, `OnProcessFinished` delegates (sync + async)
- **Virtual methods** — Format processor stream methods are virtual for subclass customization
- **Encoding-aware** — configurable encoding (UTF-8, Windows-1250, etc.)
- **CsvParser in Birko.Helpers** — RFC 4180 parser moved to Helpers for standalone reuse

**Dependencies:** `Microsoft.Extensions.Logging.Abstractions` (ILogger), `Birko.Helpers` (CsvParser)

**What stays in Affiliate.Import after migration:**
- `Processors/Common/` — feed-format processors (GoogleMerchant, Heureka, Awin, etc.) tied to `Shared.ViewModels.Product`
- `Processors/Custom/` — 25 shop-specific processor overrides
- `Helpers/ValueParser.cs` — domain-specific price/category parsing
- `Helpers/Categories.cs` — Heureka/Google taxonomy downloads

**Integration with Birko.BackgroundJobs:** `[Affiliate dependency]`

Processors compose naturally with BackgroundJobs — processors provide the parsing engine, BackgroundJobs provides the execution envelope (retries, persistence, scheduling, concurrency). The event-driven `OnItemProcessed` callback maps directly to persisting items inside a job's `ExecuteAsync`:

```
BackgroundJobProcessor (polling, retries, concurrency)
  └─ IJob<TInput>.ExecuteAsync(input, context, ct)
       └─ RemoteProcessor<ZipProcessor<XmlProcessor, T>, T>
            └─ OnItemProcessed → persist to store
```

Benefits over current Affiliate.Import console-app model:
- **Retry on feed timeout/failure** — automatic exponential backoff instead of manual/none
- **Crash recovery** — persistent queue (SQL/Redis/ES) resumes after restart
- **Scheduling** — `RecurringJobScheduler` or `ScheduleAsync()` replaces external cron
- **Concurrency** — `MaxConcurrency = N` parallel feeds instead of sequential loop
- **Priority** — critical feeds processed first via `Priority` field
- **Per-feed isolation** — named queues per site (e.g., `queueName: "yetulust_sk"`)
- **Progress tracking** — `JobStatus` lifecycle replaces Stopwatch + console output
- **Hosting flexibility** — `IHostedService` runs inside web app or standalone worker

Design note: `AbstractProcessor.ProcessAsync()` is already async and `CancellationToken`-aware, so it plugs directly into `IJob.ExecuteAsync(context, cancellationToken)` without adaptation. The `OnProcessFinished` event maps to job completion, and processor exceptions propagate to the BackgroundJobs retry mechanism naturally.

**Migration steps:**
1. Create `Birko.Data.Processors.shproj` in `C:\Source\`
2. Move 7 base files, update namespace `Affiliate.Import.Processors` → `Birko.Data.Processors`
3. Move `CsvParser` helper into project (or into `Birko.Helpers`)
4. Keep `ValueParser` and `Categories` in Affiliate.Import (domain-specific)
5. Update Affiliate.Import to reference new shared project
6. Update `Affiliate.sln` to include the new shared project
7. Common/Custom processors update `using` statements only — no logic changes
8. Wrap existing `Tasks/*.cs` static methods as `IJob<TInput>` implementations
9. Replace `Program.cs` sequential loop with `JobDispatcher.EnqueueAsync()` per feed
10. Add `RecurringJobScheduler` for scheduled imports (replaces external cron)

---

## Summary of Projects

| Phase | Core Project | Platform Projects | Status | Symbio Need |
|-------|--------------|-------------------|--------|-------------|
| 1 | **Birko.Data.Patterns** | UoW, Paging, Specification, Concurrency | ✅ Complete | Stub UoW needs full repo integration |
| 2 | **Birko.Caching** | Redis, Hybrid, NCache | ✅ Core+Redis done | Pending: replace Symbio ICacheService stub |
| 3 | **Birko.Validation** | (platform-agnostic) | ✅ Done | Pending: integrate into Symbio endpoints |
| 4 | **Birko.BackgroundJobs** | SQL, Redis | ✅ Core+SQL+Redis done | Pending: replace Symbio raw IHostedService |
| 5 | **Birko.MessageQueue** | MQTT, InMemory | ✅ Core+MQTT+InMemory done | Pending: replace Symbio direct MQTTnet usage |
| 6 | **Birko.Storage** | AzureBlob, Aws, Google, Minio | ✅ Core done, providers planned | Local impl done, cloud providers planned |
| 7 | **Birko.Messaging** | SendGrid, Razor, Mailgun, Twilio, Firebase, Apple | ✅ Core+Razor done, others planned | SMTP email, Razor templates, SMS/push interfaces |
| 8 | **Birko.MessageQueue** | RabbitMQ, Kafka, Azure, Aws, Redis, MassTransit | ⬜ Planned (Medium) | Future: remaining providers |
| 9 | **Birko.EventBus** | MessageQueue, Outbox, EventSourcing | ✅ Complete | Decoupled module communication |
| 10 | **Birko.Telemetry** | OpenTelemetry, Prometheus, Seq, Grafana | ✅ Core done, exporters planned | Store instrumentation, correlation ID middleware |
| 11 | **Birko.Security** | BCrypt, Vault, AzureKeyVault | ✅ Complete | All extensions implemented |
| 12 | **Birko.Workflow** | SQL, MongoDB | ⬜ Planned (Low) | Future: reservations, order tracking |
| 13 | Additional | Time, Health, Serialization, Localization, CQRS | ⬜ Planned (Low) | Future |
| 13 | **Birko.Data.Processors** `[Affiliate]` | (platform-agnostic) | ✅ Implemented | Affiliate Import extraction |
| — | **Birko.Data.Migrations** | SQL, MongoDB, RavenDB, ElasticSearch, InfluxDB, TimescaleDB | ✅ Done | Integrated (Symbio extends with module-awareness) |
| — | **Birko.Data.Sync** | Sql, MongoDb, RavenDB, ElasticSearch, Json, Tenant | ✅ Done | Available |
| — | **Birko.Data.Aggregates** | (platform-agnostic) | ⬜ Planned (Medium) | SQL ↔ NoSQL aggregate mapping for sync |

---

## Future Enhancements

### Birko.Data.Aggregates — SQL ↔ NoSQL Aggregate Mapper
**Status:** Planned | **Priority:** Medium

Helper library to bridge the impedance mismatch between SQL m:n relations and NoSQL nested/denormalized documents. Defines aggregate shapes once, then automatically flattens (SQL → document) and expands (document → SQL junction table ops) for sync scenarios.

**Use case:** Project uses SQL as source of truth (relational integrity, ACID) and syncs to Elasticsearch/MongoDB for search/read workloads. The mapper handles denormalization of joins into nested documents and (optionally) diffing nested arrays back into junction table insert/delete operations.

```
Birko.Data.Aggregates/                    (.shproj)
├── Core/
│   ├── IAggregateDefinition.cs           - Interface for aggregate shape definitions
│   ├── AggregateDefinition<T>.cs         - Fluent builder (HasMany, HasOne, Through)
│   ├── RelationshipType.cs               - OneToOne, OneToMany, ManyToMany enum
│   └── RelationshipDescriptor.cs         - Metadata about each relation (FKs, junction table, navigation property)
├── Mapping/
│   ├── IAggregateMapper.cs               - Flatten + Expand interface
│   ├── AggregateMapper.cs                - Core mapping logic
│   ├── FlattenResult.cs                  - Denormalized document output
│   └── SyncOperation.cs                  - Insert/Delete/Update operations for junction/child tables
├── Diff/
│   ├── CollectionDiffer.cs               - Diffs current vs desired state for m:n relations
│   └── DiffResult.cs                     - Added, Removed, Unchanged items
└── Extensions/
    └── SyncPipelineExtensions.cs         - Integration with Birko.Data.Sync providers
```

**Fluent definition API:**
```csharp
public class ProductAggregate : AggregateDefinition<Product>
{
    public ProductAggregate()
    {
        HasMany(p => p.Categories)
            .Through<ProductCategory>(j => j.ProductId, j => j.CategoryId);

        HasMany(p => p.Tags)
            .Via(t => t.ProductId);

        HasOne(p => p.DefaultImage)
            .Via(i => i.ProductId);
    }
}
```

**Phase 1 — Flatten only (SQL → NoSQL sync):**
- AggregateDefinition fluent builder
- AggregateMapper.Flatten() — composes related entities into nested document
- SyncPipelineExtensions — plugs into Birko.Data.Sync for automatic denormalization

**Phase 2 — Expand (bidirectional sync):**
- CollectionDiffer — diffs nested arrays against existing junction rows
- AggregateMapper.Expand() — generates SyncOperation[] (insert/delete on junction tables)
- Unit of Work integration via Birko.Data.Patterns for atomic expand operations

**Dependencies:** Birko.Data.Core, Birko.Data.Stores, Birko.Data.Sync, Birko.Data.Patterns (Phase 2 only)

**Estimated scope:** ~10-15 classes, similar size to Birko.Validation or Birko.Rules

**Important:** Does NOT attempt query translation (SQL joins ↔ ES nested queries). Query logic remains in store-specific repository implementations. This helper handles data shape mapping for sync only.

---

### Existing Projects

#### Birko.Data.SQL
- [ ] Add connection resiliency and retry logic
- [ ] Add bulk copy for all SQL providers (currently MSSql only)
- [ ] Add query caching for frequently executed queries
- [ ] Add database-specific optimizations

#### Birko.Data.ElasticSearch
- [ ] Add index management utilities
- [ ] Add re-indexing helpers
- [ ] Add search result highlighting

#### Birko.Data.MongoDB
- [ ] Add change stream support
- [ ] Add aggregation pipeline builders

#### Birko.Communication
- [ ] Add GraphQL client
- [ ] Add gRPC support
- [ ] Add OAuth2 helpers in Authentication

#### Birko.Models
- [ ] Add more base model types
- [ ] Add ViewModel to Model mapping utilities

---

## Technical Debt

- [x] **Birko.Data 3-way split** — Replace `Birko.Data` with three focused shared projects:
  - **Birko.Data.Core** — Models (AbstractModel, AbstractLogModel, ICopyable, IDefault, ILoadable), ViewModels (ViewModel, ModelViewModel, LogViewModel, AbstractLogViewModel), Filters (IFilter, ModelByGuid, ModelsByGuid), Exceptions (StoreException). Foundation layer everything depends on.
  - **Birko.Data.Stores** — Store interfaces (IStore, IAsyncStore, IBulkStore, IAsyncBulkStore), abstract implementations, Settings chain (Settings → PasswordSettings → RemoteSettings), OrderBy, StoreLocator, StoreExtensions, IStoreWrapper, ITransactionalStore. Depends on Birko.Data.Core.
  - **Birko.Data.Repositories** (directory exists, currently empty) — Repository interfaces, abstract implementations, RepositoryLocator, ServiceCollectionExtensions. Depends on Birko.Data.Core + Birko.Data.Stores.
  - Lightweight consumers (Birko.Storage, Birko.Caching, Birko.Models.*) would only reference Birko.Data.Core instead of pulling in all store/repository abstractions.
  - All downstream projects (Affiliate, FisData, Symbio, Birko.Data.SQL, etc.) update `.projitems` imports accordingly.
- [ ] **Models ↔ ViewModels circular dependency** — AbstractModel implements `ILoadable<ModelViewModel>` and AbstractLogModel implements `ILoadable<LogViewModel>`, creating a circular reference between Models/ and ViewModels/. Currently kept together in Birko.Data.Core (Option A). Future cleanup: remove ILoadable from Models (make mapping one-directional, ViewModels know about Models but not vice versa) to allow separating Models into a standalone project for pure DTO scenarios.
- [ ] **RetryPolicy duplication** — `Birko.BackgroundJobs.RetryPolicy` and `Birko.MessageQueue.Retry.RetryPolicy` are near-identical classes (only defaults differ). Consider extracting to a shared location (e.g., `Birko.Core`) if more projects need retry logic.
- [ ] **MqttExtensions.cs** — MQTT v5 features (topic aliases for bandwidth optimization, user properties for custom metadata). Low priority unless Symbio IoT has high-frequency sensors where topic alias savings matter.
- [x] **Rename TenantId → TenantGuid** — Renamed throughout the framework for consistency with Guid suffix convention. Affected: `ITenant`, `ITenantContext`, `TenantContext`, tenant store wrappers, tenant middleware, tenant sync, `ICurrentUser`, `JwtClaimNames`, `ClaimMappingOptions`, `EventContext`, `OutboxEntry`, `EventEnvelope`, `IRoleProvider`, and all related tests/examples. Wire-format strings preserved (`"tenant_id"` claim, `"X-Tenant-Id"` header). Downstream consumers (Symbio, FisData, Affiliate) need updating.

---

## Reference

For implementation questions, refer to:
- [CLAUDE.md](./CLAUDE.md) - Framework overview
- Individual project CLAUDE.md files for specific implementation details

---

**Last Updated:** 2026-03-16
