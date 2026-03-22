# Birko Framework Architecture

## Overview

Birko Framework is designed as a modular, layered architecture supporting multiple data storage backends, communication protocols, business models, validation, caching, security, and cross-cutting patterns.

## Layers

### 0. Contracts Layer (Birko.Contracts)

Zero-dependency foundation providing pure interfaces and shared utilities:
- **Model interfaces:** `ILoadable<T>`, `ICopyable<T>`, `IDefault`, `ITimestamped` (namespace `Birko.Data.Models`)
- **Entity interfaces:** `IGuidEntity` (Guid? property), `ILogEntity` (extends IGuidEntity + ITimestamped) — enable bidirectional Model↔ViewModel mapping without circular type references
- **Retry:** `RetryPolicy` — configurable exponential backoff, shared by BackgroundJobs and MessageQueue (namespace `Birko`)

### 1. Models Layer

#### Base Models (Birko.Data.Core)
- `AbstractModel` - Base entity implementing `IGuidEntity`, `ICopyable<AbstractModel>`, `ILoadable<IGuidEntity>`
- `AbstractLogModel` - Extends AbstractModel, implements `ITimestamped`, `ILoadable<ILogEntity>`

Models reference `IGuidEntity`/`ILogEntity` interfaces (from Birko.Contracts), not ViewModel types. ViewModels reference Model types. This breaks the circular dependency.

#### Business Models (Birko.Models)
- **Birko.Models.Contracts** - Domain interfaces (ICatalogItem, IPriceable, IVariantable, ICategorizeable, IBatchable, ILocatable, IHierarchical, IDocument, IContactable, IAddressable)
- **Birko.Models** - Base models (`AbstractPercentage`, `AbstractTree`, `ValueData`, `SourceValue`) + Value Objects (`Money`, `MoneyWithTax`, `Percentage`, `PostalAddress`, `Quantity`)
- **Birko.Models.Product** - Product-specific models (implements ICatalogItem)
- **Birko.Models.Category** - Category-specific models (implements IHierarchical)
- **Birko.Models.SEO** - SEO-specific models
- **Birko.Models.Accounting** - Currency, Tax, PriceGroup, MeasureUnit (legacy — use Pricing for new consumers)
- **Birko.Models.Customers** - Address, Customer, InvoiceAddress (implements IAddressable, IContactable)
- **Birko.Models.Users** - User, UserAgenda, Agenda
- **Birko.Models.Warehouse** - Item, ItemVariant, Repository, WareHouseDocument (legacy — use Inventory for new consumers)
- **Birko.Models.Inventory** - StockItem, StockItemVariant, StorageLocation, StockMovement, InventoryDocument (clean, no SQL attrs)
- **Birko.Models.Pricing** - Currency, Tax, PriceGroup, PriceList, PriceListEntry, Discount (clean, no SQL attrs)
- **Birko.Models.SQL** - Fluent SQL mapping framework (ModelMap, IModelMapping, ModelMapRegistry)

#### Namespace Convention
- **Models:** Base namespace (e.g., `Birko.Models.Product`)
- **ViewModels:** `.ViewModels` sub-namespace (e.g., `Birko.Models.Product.ViewModels`)
- **Filters:** `.Filters` sub-namespace (e.g., `Birko.Models.Product.Filters`)

### 2. Data Access Layer

#### Core Interfaces (Birko.Data.Stores)

| Interface | Description |
|-----------|-------------|
| `IStore<T>` | Sync CRUD: Create, Read (by Guid or Expression filter), Update, Delete, Count, Save, Init, Destroy |
| `IAsyncStore<T>` | Async equivalents with CancellationToken support |
| `IBulkStore<T>` | Extends IStore with bulk Read, Create, Update, Delete + ordering/paging |
| `IAsyncBulkStore<T>` | Async bulk equivalents with CancellationToken support |

All store interfaces constrain `T : AbstractModel`.

#### Abstract Implementations

```
AbstractStore<T> where T : AbstractModel
    -> AbstractBulkStore<T>

AbstractAsyncStore<T> where T : AbstractModel
    -> AbstractAsyncBulkStore<T>
```

#### SQL Layer (Birko.Data.SQL)

```
DataBaseStore<DB, T> where DB : AbstractConnector, T : AbstractModel
    -> DataBaseBulkStore<DB, T>

AsyncDataBaseStore<DB, T> where DB : AbstractConnector, T : AbstractModel
    -> AsyncDataBaseBulkStore<DB, T>
```

SQL stores implement `ISettingsStore<ISettings>`, `ISettingsStore<PasswordSettings>`, and transactional interfaces (`ITransactionalStore`/`IAsyncTransactionalStore`).

#### SQL Connectors

```
AbstractConnectorBase (shared: settings, type conversion, field definitions)
    -> AbstractConnector (sync: DoCommand, DoInit, events)
    -> AbstractAsyncConnector (async: DoCommandAsync, DoInitAsync, events)
```

Note: `AbstractConnector` and `AbstractAsyncConnector` are **siblings**, not parent-child.

#### Database Providers
- **MSSql** - Microsoft SQL Server (`MSSqlConnector`)
- **PostgreSQL** - PostgreSQL (`PostgreSQLConnector`)
- **MySQL** - MySQL (`MySQLConnector`)
- **SqLite** - SQLite (`SqLiteConnector`)
- **TimescaleDB** - TimescaleDB time-series

All connectors are in `Birko.Data.SQL.Connectors` namespace.

#### NoSQL Providers
- **ElasticSearch** - Elasticsearch search engine (`ElasticSearchStore<T>`)
- **MongoDB** - MongoDB document database (`MongoDBStore<T>`, uses `MongoDBModel`)
- **RavenDB** - RavenDB document database
- **InfluxDB** - InfluxDB time-series database
- **JSON** - File-based JSON storage (`JsonStore<T>`)

#### Repositories

```
AbstractRepository<T> where T : AbstractModel
    -> AbstractBulkRepository<T>

AbstractAsyncRepository<T> where T : AbstractModel
    -> AbstractAsyncBulkRepository<T>
```

SQL-specific repositories:
- `DataBaseRepository<T, DB>` - Sync SQL repository
- `AsyncDataBaseRepository<T, DB>` - Async SQL repository

#### ViewModel Layer

```
ViewModel -> ModelViewModel (adds Guid, implements IGuidEntity) -> LogViewModel (adds timestamps, implements ILogEntity)
AbstractLogViewModel extends ViewModel directly (implements ILogEntity with Guid)
```

ViewModel repositories provide mapping between models and view models:
- **Birko.Data.ViewModel** - Base abstractions
- **Birko.Data.SQL.ViewModel** - SQL ViewModel repositories
- **Birko.Data.ElasticSearch.ViewModel** - Elasticsearch ViewModel repositories
- **Birko.Data.InfluxDB.ViewModel** - InfluxDB ViewModel repositories
- **Birko.Data.JSON.ViewModel** - JSON ViewModel repositories
- **Birko.Data.MongoDB.ViewModel** - MongoDB ViewModel repositories
- **Birko.Data.RavenDB.ViewModel** - RavenDB ViewModel repositories
- **Birko.Data.TimescaleDB.ViewModel** - TimescaleDB ViewModel repositories

### 3. Feature Layers

#### Migrations (Birko.Data.Migrations)
Database schema migration framework with provider-specific implementations:
- `AbstractMigration` - Base class with `Version` (long), `Name`, `Up()`, `Down()`
- `SqlMigration` - SQL-specific with `ExecuteSql()`, `UpSql`/`DownSql` properties, helper methods (`TableExists`, `ColumnExists`)
- Provider-specific: SQL, ElasticSearch, MongoDB, RavenDB, InfluxDB, TimescaleDB

#### Synchronization (Birko.Data.Sync)
Data synchronization between different storage backends with provider-specific implementations:
- SQL, ElasticSearch, MongoDB, RavenDB, JSON sync providers
- Tenant-aware sync (`Birko.Data.Sync.Tenant`)

#### Multi-Tenancy (Birko.Data.Tenant)
Tenant-aware data access and filtering via `ITenant` interface and `TenantContext`.

#### Event Sourcing (Birko.Data.EventSourcing)
Event pattern implementation with:
- `IEvent` - Domain event with EventId, AggregateId, Version, EventType, OccurredAt, EventData, Metadata, UserId
- Event store and aggregate root patterns

#### Patterns (Birko.Data.Patterns)
Cross-cutting concerns:
- **Unit of Work** - `IUnitOfWork` / `IUnitOfWork<TContext>` with `BeginAsync`, `CommitAsync`, `RollbackAsync`
- **Specification** - `ISpecification<T>` with `IsSatisfiedBy()` and `ToExpression()`
- **Paging** - `IPagedRepository<T>` / `IAsyncPagedRepository<T>` with `ReadPaged()`/`ReadPagedAsync()`
- **Soft Delete** - `ISoftDeletable` model interface with `DeletedAt`
- **Audit** - `IAuditable` model interface with `CreatedBy`, `UpdatedBy`

#### SQL Views (Birko.Data.SQL.View)
Dynamic SQL view generation from attributes.

### 4. Validation Layer (Birko.Validation)
Fluent validation framework:
- `IValidator<T>` - Validate/ValidateAsync returning `ValidationResult`
- `AbstractValidator<T>` - Base class with `RuleFor<TProp>()` fluent API
- Built-in rules and store-aware wrappers

### 5. Caching Layer (Birko.Caching)
Unified caching interface:
- `ICache` - GetAsync, SetAsync, RemoveAsync, ExistsAsync, GetOrSetAsync, RemoveByPrefixAsync, ClearAsync
- `CacheEntryOptions` - TTL configuration
- **MemoryCache** - In-process cache
- **RedisCache** (Birko.Caching.Redis) - Distributed Redis backend with `RedisConnectionManager`

### 6. Security Layer (Birko.Security)
- `IPasswordHasher` - Hash/Verify (`Pbkdf2PasswordHasher` implementation)
- `IEncryptionProvider` - Encrypt/Decrypt bytes and strings (`AesEncryptionProvider` - AES-256-GCM)
- `ITokenProvider` - GenerateToken/ValidateToken with `TokenResult`, `TokenValidationResult`, `TokenOptions`
- **Birko.Security.Jwt** - JWT implementation of ITokenProvider (`JwtTokenProvider`)
- Static token authentication (moved from Birko.Communication.Authentication)
- RBAC interfaces

### 7. Background Jobs Layer (Birko.BackgroundJobs)
Job processing framework with pluggable persistent queues:
- `IJob` / `IJob<TInput>` - Job contracts (parameterless and typed)
- `IJobQueue` - Storage interface (enqueue, dequeue, complete, fail)
- `JobDescriptor` - Persistence model with status, retries, priority, scheduling
- `BackgroundJobProcessor` - Concurrent polling processor with semaphore-based concurrency
- `JobDispatcher` - Fluent enqueue/schedule/cancel API
- `RecurringJobScheduler` - Interval-based recurring job registration
- `InMemoryJobQueue` - Non-persistent queue for testing

Queue backends:
- **SQL** (`Birko.BackgroundJobs.SQL`) - `SqlJobQueue<DB>` using any SQL connector
- **Elasticsearch** (`Birko.BackgroundJobs.ElasticSearch`) - `ElasticSearchJobQueue`
- **MongoDB** (`Birko.BackgroundJobs.MongoDB`) - `MongoDBJobQueue`
- **RavenDB** (`Birko.BackgroundJobs.RavenDB`) - `RavenDBJobQueue`
- **JSON** (`Birko.BackgroundJobs.JSON`) - `JsonJobQueue` for dev/testing
- **Redis** (`Birko.BackgroundJobs.Redis`) - `RedisJobQueue` via StackExchange.Redis

### 8. Message Queue Layer (Birko.MessageQueue)
Asynchronous messaging abstractions for pub/sub and point-to-point patterns:
- `IMessageQueue` - Combined producer + consumer with connection management
- `IMessageProducer` / `IMessageConsumer` - Send and receive messages
- `IMessageHandler<T>` - Typed message handler
- `ISubscription` - Active subscription handle (dispose to unsubscribe)
- `QueueMessage` - Message wrapper (Id, Body, Headers, Priority, TTL, Delay)
- `MessageHeaders` - Metadata (CorrelationId, ReplyTo, ContentType, GroupId)

Messaging patterns:
- **IPublisher / ISubscriber** - Pub/Sub (one-to-many)
- **ISender / IReceiver** - Point-to-point (one-to-one)
- **ITransactionalProducer** - Atomic message batches

Infrastructure:
- `IMessageSerializer` - Pluggable serialization (`JsonMessageSerializer` default)
- `RetryPolicy` - Exponential backoff for failed deliveries
- `DeadLetterOptions` - DLQ routing for exhausted retries

Queue backends:
- **InMemory** (`Birko.MessageQueue.InMemory`) - Channel-based in-process queue for testing/dev
- **MQTT** (`Birko.MessageQueue.MQTT`) - MQTTnet-based client for IoT/sensor messaging

### 9. Event Bus Layer (Birko.EventBus)
In-process and distributed event-driven architecture:
- `IEvent` - Marker interface for domain events (EventId, OccurredAt, CorrelationId)
- `IEventBus` - Publish events, register handlers
- `IEventHandler<T>` - Typed event handler
- `IPipelineBehavior<T>` - Cross-cutting pipeline (logging, validation, enrichment)
- `IEventDeduplicator` - Prevent duplicate event processing
- DI extensions for handler auto-registration

Distribution layers:
- **Birko.EventBus.MessageQueue** - Distributed event bus over any Birko.MessageQueue provider (EventEnvelope, AutoSubscriber, HostedService)
- **Birko.EventBus.Outbox** - Transactional outbox pattern (OutboxEventBus decorator, OutboxProcessor, IOutboxStore)
- **Birko.EventBus.EventSourcing** - EventStore-to-EventBus bridge (DomainEventPublished, EventStoreEventBus decorator, EventReplayService)

### 10. Storage Layer (Birko.Storage)
File and blob storage abstraction:
- `IFileStorage` - Upload, Download, Delete, Exists, List, Copy, Move (async, stream-based)
- `StorageResult<T>` - Distinguishes "not found" from success
- `FileReference` - Metadata (path, size, content type, ETag, timestamps)
- `StorageOptions` - MaxFileSize, AllowedContentTypes, OverwriteExisting, custom Metadata
- `IPresignedUrlStorage` - Optional interface for cloud providers (presigned upload/download URLs)
- **LocalFileStorage** - Built-in filesystem implementation with `.meta.json` companion files
- Path security: rejects traversal attacks, absolute paths, null bytes, control characters

### 11. Messaging Layer (Birko.Messaging)
Unified interfaces for email, SMS, and push notifications:
- `IMessage` - Base message interface (Recipients, Body, Metadata, ScheduledAt)
- `IMessageSender<T>` - Generic sender with batch support
- `MessageResult` - Static factory (Succeeded/Failed), never throws for delivery failures
- `MessageAddress` - Recipient value object (email, phone, device token)
- **EmailMessage** - Full email with CC, BCC, ReplyTo, attachments, HTML/plain text
- **SmtpEmailSender** - Built-in SMTP implementation via System.Net.Mail
- **ITemplateEngine** / **StringTemplateEngine** - `{{placeholder}}` templates with nested property support
- **SmsMessage** / **PushMessage** - Interface-only (provider implementations planned)

### 12. Communication Layer

#### Base (Birko.Communication)
- `AbstractPort` with `SubscribeProcessData()` delegate pattern for data processing

#### Protocols
- **Network** - TCP/UDP (`TcpIp`, `Udp` ports)
- **Hardware** - Serial, LPT, Infraport
- **Bluetooth** - Bluetooth, BluetoothLE
- **WebSocket** - `WebSocketServer` with middleware pipeline
- **REST** - `RestClient` (string-based responses)
- **SOAP** - SOAP client
- **SSE** - Server-Sent Events with middleware
- **Modbus** - Modbus RTU/TCP (serial/network)
- **Camera** - FFmpeg-based JPEG frame capture
- **OAuth** - OAuth2 client (Client Credentials, Auth Code, PKCE, Device Code) with automatic token caching and `OAuthDelegatingHandler`

### 13. Security Layer — ASP.NET Core Integration (Birko.Security.AspNetCore)
Bridges Birko.Security into ASP.NET Core:
- `AddBirkoSecurity()` - One-line DI registration (JWT Bearer auth, ICurrentUser, IPermissionChecker, ITenantResolver, ITenantContext)
- `ICurrentUser` / `ClaimsCurrentUser` - Access authenticated user (UserId, Email, TenantId, Roles, Permissions)
- `ClaimsPermissionChecker` - Permission checking from JWT claims (supports wildcard `"*"`)
- `RequirePermission()` - Minimal API endpoint filter
- **Tenant Resolution** - Header (`X-Tenant-Id`), Subdomain, or Custom via `ITenantResolver`
- `TenantMiddleware` - Per-request tenant resolution into scoped `ITenantContext`
- `TokenServiceAdapter` - Structured request/response wrapper around ITokenProvider

### 14. Redis Infrastructure (Birko.Redis)
Shared Redis connection management:
- `RedisSettings` - Extends `RemoteSettings` with Redis-specific configuration
- `RedisConnectionManager` - Singleton connection multiplexer, used by Birko.Caching.Redis and Birko.BackgroundJobs.Redis

### 15. Telemetry Layer (Birko.Telemetry)
Thin instrumentation over .NET built-in APIs (`System.Diagnostics.Metrics`, `System.Diagnostics.Activity`):
- `BirkoTelemetryConventions` - Standard meter, activity source, metric, and tag names
- `StoreInstrumentation` - Internal helper: Meter, ActivitySource, Histogram, Counters; sync/async Execute overloads
- `InstrumentedStoreWrapper<TStore, T>` - Decorator for `IStore<T>` with metrics and tracing
- `InstrumentedBulkStoreWrapper<TStore, T>` - Extends above for `IBulkStore<T>` (bulk flag in tags)
- `AsyncInstrumentedStoreWrapper<TStore, T>` - Decorator for `IAsyncStore<T>`
- `AsyncInstrumentedBulkStoreWrapper<TStore, T>` - Extends above for `IAsyncBulkStore<T>`
- `CorrelationIdMiddleware` - ASP.NET Core middleware: reads/generates `X-Correlation-Id`, sets Activity baggage
- `BirkoTelemetryOptions` - EnableCorrelationId, CorrelationIdHeaderName
- `AddBirkoTelemetry()` / `UseBirkoCorrelationId()` - DI and middleware registration extensions
- Fluent API: `.WithInstrumentation()`, `.WithBulkInstrumentation()`, `.WithAsyncInstrumentation()`, `.WithAsyncBulkInstrumentation()`
- No external NuGet dependencies

### 16. Helper Libraries
- **Birko.Helpers** - StringHelper, HtmlHelper, ObjectHelper, EnumerableHelper, PathValidator
- **Birko.Structures** - Generic data structures (Tree, AVLTree, BinaryNode)

## Settings Chain (Birko.Configuration)

Settings classes live in the `Birko.Configuration` shared project (namespace `Birko.Configuration`). `Birko.Data.Stores` imports them transitively. Lightweight consumers (MessageQueue, Communication) can import `Birko.Configuration` directly without pulling in the full store abstraction layer. `Birko.Configuration` imports `Birko.Contracts` for `ILoadable<T>`.

```
ISettings (GetId)
    -> Settings (Location, Name)
        -> PasswordSettings (+Password)
            -> RemoteSettings (+UserName, +Port)
```

- `Settings` - File-based stores (JSON, SQLite)
- `PasswordSettings` - Password-protected stores (SQLite with encryption)
- `RemoteSettings` - Network stores (SQL Server, PostgreSQL, MySQL, MongoDB, ElasticSearch)

## Design Patterns

| Pattern | Usage |
|---------|-------|
| **Repository** | Separates data access from business logic |
| **Store** | Unified interface for different storage backends |
| **Abstract Factory** | Creating connectors and stores |
| **Strategy** | SQL dialects and query strategies |
| **Template Method** | Abstract base classes define algorithms |
| **Unit of Work** | Transaction batching across stores |
| **Specification** | Composable query predicates |
| **Decorator** | Cache, audit, soft-delete, instrumentation wrappers |

## Dependency Flow

```
Birko.Contracts (zero deps)
  -> Birko.Configuration (settings)
  -> Birko.Data.Core (models, ViewModels, filters)
       -> Birko.Data.Stores (store interfaces, imports Configuration)
            -> Birko.Data.Repositories (repository interfaces)
            -> Provider Implementations (SQL, NoSQL, JSON)
            -> ViewModel Repositories
            -> Features (Migrations, Sync, Tenant, EventSourcing, Patterns)
  -> Birko.BackgroundJobs (RetryPolicy from Contracts)
  -> Birko.MessageQueue (RetryPolicy from Contracts)

Birko.Time.Abstractions (zero deps)
  -> Birko.Time (calendars, working hours)

Validation, Caching, Security, EventBus, Storage, Messaging, Telemetry
```

## Extensibility

### Adding a New Store
1. Inherit from `AbstractStore<T>` (sync) or `AbstractAsyncStore<T>` (async)
2. Implement abstract methods (Create, Read, Update, Delete, Count, Init, Destroy)
3. For SQL: inherit from `DataBaseStore<DB, T>` or `AsyncDataBaseStore<DB, T>`
4. For bulk: extend to `AbstractBulkStore<T>` or `AbstractAsyncBulkStore<T>`
5. Implement `ISettingsStore<>` if settings are needed

### Adding a New Repository
1. Inherit from `AbstractRepository<T>` or `AbstractAsyncRepository<T>`
2. For SQL: inherit from `DataBaseRepository<T, DB>` or `AsyncDataBaseRepository<T, DB>`
3. Add custom business methods
4. Implement `SetSettings()` for settings pass-through

### Adding a New Connector
1. Inherit from `AbstractConnector` (sync) or `AbstractAsyncConnector` (async)
2. Implement `CreateConnection()`, `ConvertType()`, `FieldDefinition()`
3. Register provider-specific SQL dialect strategies
