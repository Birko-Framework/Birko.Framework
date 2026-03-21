# Birko Framework

## Overview
Birko Framework is a modular .NET framework providing data access, communication, and model infrastructure for enterprise applications.

## Project Structure

### Core Projects
- **Birko.Framework** - Main framework application (.NET 10.0, shared projects via .projitems)
- **Birko.Contracts** - Pure interfaces (ILoadable, ICopyable, IDefault, ITimestamped) with zero dependencies
- **Birko.Data.Core** - Models, ViewModels, Filters, Exceptions (foundation layer, imports Birko.Contracts)
- **Birko.Configuration** - Settings hierarchy (Settings, PasswordSettings, RemoteSettings) in namespace `Birko.Configuration`, imports Birko.Contracts
- **Birko.Data.Stores** - Store interfaces/abstractions, OrderBy, StoreLocator (imports Birko.Configuration transitively)
- **Birko.Data.Repositories** - Repository interfaces/abstractions, RepositoryLocator, DI extensions
- **Birko.Models** - Base models and extensions

### Data Layer
- **Birko.Data.SQL** - SQL base classes (DataBaseStore, DataBaseBulkStore, AsyncDataBaseStore, AsyncDataBaseBulkStore)
- **Birko.Data.SQL.MSSql** - Microsoft SQL Server implementation
- **Birko.Data.SQL.PostgreSQL** - PostgreSQL implementation
- **Birko.Data.SQL.MySQL** - MySQL implementation
- **Birko.Data.SQL.SqLite** - SQLite implementation
- **Birko.Data.JSON** - JSON file-based storage
- **Birko.Data.ElasticSearch** - Elasticsearch repository/store
- **Birko.Data.MongoDB** - MongoDB repository/store
- **Birko.Data.RavenDB** - RavenDB repository/store
- **Birko.Data.InfluxDB** - InfluxDB time-series database
- **Birko.Data.TimescaleDB** - TimescaleDB implementation

### ViewModel Layer
- **Birko.Data.ViewModel** - Base ViewModel repository abstractions
- **Birko.Data.SQL.ViewModel** - SQL ViewModel repositories
- **Birko.Data.ElasticSearch.ViewModel** - Elasticsearch ViewModel repositories
- **Birko.Data.InfluxDB.ViewModel** - InfluxDB ViewModel repositories
- **Birko.Data.JSON.ViewModel** - JSON ViewModel repositories
- **Birko.Data.MongoDB.ViewModel** - MongoDB ViewModel repositories
- **Birko.Data.RavenDB.ViewModel** - RavenDB ViewModel repositories
- **Birko.Data.TimescaleDB.ViewModel** - TimescaleDB ViewModel repositories

### Data Features
- **Birko.Data.Patterns** - Cross-cutting patterns (Unit of Work, Soft Delete, Audit, Timestamp, Paging)
- **Birko.Data.Migrations** - Database migration framework
- **Birko.Data.Migrations.SQL** - SQL migrations
- **Birko.Data.Migrations.ElasticSearch** - Elasticsearch migrations
- **Birko.Data.Migrations.MongoDB** - MongoDB migrations
- **Birko.Data.Migrations.RavenDB** - RavenDB migrations
- **Birko.Data.Migrations.InfluxDB** - InfluxDB migrations
- **Birko.Data.Migrations.TimescaleDB** - TimescaleDB migrations
- **Birko.Data.Sync** - Data synchronization framework
- **Birko.Data.Sync.Sql** - SQL sync provider
- **Birko.Data.Sync.ElasticSearch** - Elasticsearch sync provider
- **Birko.Data.Sync.MongoDb** - MongoDB sync provider
- **Birko.Data.Sync.RavenDB** - RavenDB sync provider
- **Birko.Data.Sync.Json** - JSON sync provider
- **Birko.Data.Sync.Tenant** - Tenant-aware sync
- **Birko.Data.Aggregates** - SQL ↔ NoSQL aggregate mapper (flatten/expand for sync scenarios)
- **Birko.Data.Tenant** - Multi-tenancy support
- **Birko.Data.EventSourcing** - Event sourcing pattern implementation
- **Birko.Structures** - Data structures (trees, etc.)
- **Birko.Data.SQL.View** - SQL view generation and persistent view DDL (CREATE/DROP VIEW)
- **Birko.Data.SQL.MSSql.View** - SQL Server-specific view DDL (CREATE OR ALTER VIEW, sys.views)
- **Birko.Data.SQL.PostgreSQL.View** - PostgreSQL-specific view DDL (materialized views, information_schema)
- **Birko.Data.SQL.MySQL.View** - MySQL-specific view DDL (information_schema.VIEWS)
- **Birko.Data.SQL.SqLite.View** - SQLite-specific view DDL (CREATE VIEW IF NOT EXISTS, sqlite_master)
- **Birko.Helpers** - Data helper utilities
- **Birko.Data.Processors** - Generic stream processors (XML, CSV, HTTP, ZIP) with decorator composition
- **Birko.Data.Repositories** - Shared repository project (reserved)

### Tests
- **Birko.Data.Tests** - Unit tests for core data stores (AbstractAsyncStore)
- **Birko.Data.SQL.Tests** - Unit tests for SQL connectors, strategies, and expression parsing
- **Birko.Data.ElasticSearch.Tests** - Unit tests for Elasticsearch expression parsing
- **Birko.Helpers.Tests** - Unit tests for helper utilities (EnumerableHelper)
- **Birko.Structures.Tests** - Unit tests for data structures (AVL tree, BST)
- **Birko.BackgroundJobs.Tests** - Unit tests for background job processing (queue, dispatcher, executor, processor, scheduler)
- **Birko.MessageQueue.Tests** - Unit tests for message queue (core, InMemory, MQTT topics, serialization)
- **Birko.MessageQueue.Redis.Tests** - Unit tests for Redis Streams message queue (settings, queue lifecycle, producer/consumer validation)
- **Birko.EventBus.Tests** - Unit tests for event bus (core, distributed, outbox, event sourcing)
- **Birko.Security.AspNetCore.Tests** - Unit tests for ASP.NET Core security integration (JWT auth, ICurrentUser, permissions, tenant resolution, middleware)
- **Birko.Security.BCrypt.Tests** - Unit tests for BCrypt password hashing
- **Birko.Security.Vault.Tests** - Unit tests for HashiCorp Vault secret provider
- **Birko.Security.AzureKeyVault.Tests** - Unit tests for Azure Key Vault secret provider
- **Birko.Storage.Tests** - Unit tests for file storage (core types, LocalFileStorage, extensions)
- **Birko.Storage.AzureBlob.Tests** - Unit tests for Azure Blob Storage (settings, path validation, presigned URLs, constructor validation)
- **Birko.Telemetry.Tests** - Unit tests for telemetry (conventions, store wrappers, metrics, middleware)
- **Birko.Telemetry.OpenTelemetry.Tests** - Unit tests for OpenTelemetry integration (options, DI registration, provider resolution)
- **Birko.Rules.Tests** - Unit tests for rule engine (core types, contexts, evaluator)
- **Birko.Data.Processors.Tests** - Unit tests for data processors (CSV parser, XML/CSV/ZIP processors, HTTP transport, event wiring)
- **Birko.Data.Aggregates.Tests** - Unit tests for aggregate mapper (definitions, flatten, expand, diff, sync extensions)
- **Birko.Communication.IR.Tests** - Unit tests for IR communication (NEC/Samsung/RC5 protocol encode/decode, raw protocol, IrTiming, IrCommand)
- **Birko.Communication.NFC.Tests** - Unit tests for NFC communication (tag data, NDEF records/parsing, ISO 14443A classification, HID transport, settings)
- **Birko.Security.NFC.Tests** - Unit tests for NFC authentication (auth provider, enroll/authenticate/revoke, UID normalization, expiration, in-memory store)

### Messaging
- **Birko.Messaging** - Core messaging interfaces (IMessageSender, IEmailSender, ISmsSender, IPushSender), SMTP email sender, string template engine
- **Birko.Messaging.Razor** - Razor template engine (RazorLight-based ITemplateEngine, file-based .cshtml templates, caching)
- **Birko.Messaging.Tests** - Unit tests for messaging (core types, email, templates)
- **Birko.Messaging.Razor.Tests** - Unit tests for Razor template engine (inline rendering, file templates, caching, error handling)

### Communication Layer
- **Birko.Communication** - Base communication interfaces
- **Birko.Communication.Network** - Network communication
- **Birko.Communication.Hardware** - Hardware communication
- **Birko.Communication.Bluetooth** - Bluetooth communication
- **Birko.Communication.WebSocket** - WebSocket implementation
- **Birko.Communication.REST** - REST API client
- **Birko.Communication.SOAP** - SOAP client
- **Birko.Communication.SSE** - Server-Sent Events
- **Birko.Communication.Modbus** - Modbus RTU/TCP communication (serial/network, function codes 01-06/15-16)
- **Birko.Communication.OAuth** - OAuth2 client (Client Credentials, Authorization Code, PKCE, Device Code, Refresh Token, DelegatingHandler)
- **Birko.Communication.Camera** - Camera frame capture (ICameraSource, FFmpeg-based JPEG snapshots)
- **Birko.Communication.IR** - Consumer IR (38 kHz, NEC/Samsung/RC5 protocols, pluggable transports: Serial, HTTP, MQTT, GPIO)
- **Birko.Communication.NFC** - NFC/RFID tag reading (ISO 14443A, NDEF, pluggable transports: Serial, HTTP, HID keyboard emulation)

### Models
- **Birko.Models** - Base models (AbstractPercentage, AbstractTree, ValueData, SourceValue)
- **Birko.Models.Product** - Product models
- **Birko.Models.Category** - Category models
- **Birko.Models.SEO** - SEO models
- **Birko.Models.Accounting** - Accounting models (Currency, Tax, PriceGroup, MeasureUnit)
- **Birko.Models.Customers** - Customer models (Address, Customer, InvoiceAddress)
- **Birko.Models.Users** - User, authentication (UserLogin), profile (UserProfile), RBAC (Role, RolePermission, UserRole), Agenda
- **Birko.Models.Warehouse** - Warehouse/Inventory models (Item, ItemVariant, Repository, WareHouseDocument)

### Validation
- **Birko.Validation** - Fluent validation framework (IValidator<T>, AbstractValidator<T>, built-in rules, store wrappers)

### Rules
- **Birko.Rules** - Data-driven rule engine (IRule, Rule, RuleGroup, RuleSet, RuleEvaluator, comparison operators, dictionary/object contexts)
- **Birko.Rules.Tests** - Unit tests for rule engine (core types, contexts, evaluator with all operators)

### CQRS
- **Birko.CQRS** - Command Query Responsibility Segregation (ICommand, IQuery, IRequestHandler, IPipelineBehavior, IMediator, Mediator with DI)
- **Birko.CQRS.Tests** - Unit tests for CQRS (Unit struct, mediator dispatch, pipeline behaviors, DI registration)

### Workflow
- **Birko.Workflow** - State machine engine (WorkflowBuilder, WorkflowEngine, trigger-based transitions, guards, actions, Mermaid/DOT visualization, IWorkflowInstanceStore persistence contract)
- **Birko.Workflow.SQL** - SQL workflow instance persistence (any SQL connector)
- **Birko.Workflow.ElasticSearch** - Elasticsearch workflow instance persistence
- **Birko.Workflow.MongoDB** - MongoDB workflow instance persistence
- **Birko.Workflow.RavenDB** - RavenDB workflow instance persistence
- **Birko.Workflow.JSON** - JSON file-based workflow instance persistence (dev/testing)
- **Birko.Workflow.Tests** - Unit tests for workflow engine (builder validation, transitions, guards, actions, visualization, DI)

### Serialization
- **Birko.Serialization** - Unified serialization abstraction (ISerializer, SerializationFormat, SystemJsonSerializer, SystemXmlSerializer)
- **Birko.Serialization.Newtonsoft** - Newtonsoft.Json implementation (NewtonsoftJsonSerializer)
- **Birko.Serialization.MessagePack** - MessagePack binary serialization (MessagePackBinarySerializer, ContractlessStandardResolver)
- **Birko.Serialization.Protobuf** - Protocol Buffers serialization (ProtobufBinarySerializer, protobuf-net)
- **Birko.Serialization.Tests** - Unit tests for all serializer implementations

### Caching
- **Birko.Caching** - Unified caching interface (ICache, MemoryCache, CacheSerializer)
- **Birko.Caching.Redis** - Redis backend (RedisCache)
- **Birko.Caching.Hybrid** - L1 memory + L2 distributed two-tier cache (HybridCache)

### Redis
- **Birko.Redis** - Shared Redis infrastructure (RedisSettings extending RemoteSettings, RedisConnectionManager)

### Security
- **Birko.Security** - Password hashing (PBKDF2), AES-256-GCM encryption, token provider interfaces, secret provider interface (ISecretProvider, SecretResult), static token auth (moved from Birko.Communication.Authentication), RBAC interfaces
- **Birko.Security.BCrypt** - BCrypt password hashing (pure C# Blowfish implementation, configurable work factor, NeedsRehash support)
- **Birko.Security.Vault** - HashiCorp Vault secret provider (ISecretProvider, KV v1/v2, HTTP API, VaultSettings extends PasswordSettings)
- **Birko.Security.AzureKeyVault** - Azure Key Vault secret provider (ISecretProvider, OAuth2 client credentials, REST API, AzureKeyVaultSettings extends RemoteSettings)
- **Birko.Security.Jwt** - JWT implementation of ITokenProvider
- **Birko.Security.AspNetCore** - ASP.NET Core integration: JWT Bearer authentication setup, ICurrentUser from claims, ClaimsPermissionChecker, tenant resolution middleware (header/subdomain), Minimal API permission endpoint filters, one-line DI via AddBirkoSecurity()
- **Birko.Security.NFC** - NFC-based authentication (tag-to-user mapping, enrollment, revocation, JWT integration via ITokenProvider)

### Background Jobs
- **Birko.BackgroundJobs** - Job interfaces, in-memory queue, processor, dispatcher, recurring scheduler
- **Birko.BackgroundJobs.SQL** - SQL-based persistent job queue (any SQL connector)
- **Birko.BackgroundJobs.ElasticSearch** - Elasticsearch-based persistent job queue
- **Birko.BackgroundJobs.MongoDB** - MongoDB-based persistent job queue
- **Birko.BackgroundJobs.RavenDB** - RavenDB-based persistent job queue
- **Birko.BackgroundJobs.JSON** - JSON file-based job queue (dev/testing)
- **Birko.BackgroundJobs.Redis** - Redis-based persistent job queue (StackExchange.Redis)

### Message Queue
- **Birko.MessageQueue** - Core interfaces (IMessageQueue, IMessageProducer, IMessageConsumer, IMessageHandler), patterns (Pub/Sub, Point-to-Point), serialization, retry, transactions
- **Birko.MessageQueue.InMemory** - In-memory channel-based queue (testing/development)
- **Birko.MessageQueue.MQTT** - MQTT implementation via MQTTnet (IoT, sensors, telemetry)
- **Birko.MessageQueue.Redis** - Redis Streams implementation (persistent, consumer groups, XACK)

### Event Bus
- **Birko.EventBus** - Core interfaces (IEvent, IEventBus, IEventHandler), in-process bus, pipeline behaviors, enrichment, deduplication, DI extensions
- **Birko.EventBus.MessageQueue** - Distributed event bus over Birko.MessageQueue (EventEnvelope, AutoSubscriber, HostedService)
- **Birko.EventBus.Outbox** - Transactional outbox pattern (OutboxEventBus decorator, OutboxProcessor, IOutboxStore, InMemoryOutboxStore)
- **Birko.EventBus.EventSourcing** - EventStore-to-EventBus bridge (DomainEventPublished, EventStoreEventBus decorator, EventReplayService)

### Health
- **Birko.Health** - Health check framework (IHealthCheck, HealthCheckRunner, HealthReport, DiskSpaceHealthCheck, MemoryHealthCheck)
- **Birko.Health.Data** - Infrastructure health checks (SQL, Elasticsearch, MongoDB, RavenDB, InfluxDB, Vault, MQTT, SMTP)
- **Birko.Health.Redis** - Redis health check (PING + latency)
- **Birko.Health.Azure** - Azure health checks (Blob Storage, Key Vault)
- **Birko.Health.Tests** - Unit tests for health checks (core, system, data, runner)
- **Birko.Health.Azure.Tests** - Unit tests for Azure health checks (Blob Storage, Key Vault)

### Storage
- **Birko.Storage** - File/blob storage abstraction (IFileStorage, LocalFileStorage, StorageResult, FileReference, presigned URL support)
- **Birko.Storage.AzureBlob** - Azure Blob Storage provider (REST API, OAuth2, SAS presigned URLs, no SDK dependency)

### Telemetry
- **Birko.Telemetry** - Thin instrumentation layer: store metrics (duration/count/errors via System.Diagnostics.Metrics), distributed tracing (ActivitySource), correlation ID middleware, store wrapper decorators
- **Birko.Telemetry.OpenTelemetry** - OpenTelemetry SDK integration: AddBirkoOpenTelemetry() DI extension, auto-wires Birko meters/activity sources, OTLP + Console exporters, ASP.NET Core instrumentation, configurable service resource

### Processors
- **Birko.Data.Processors** - Generic stream processor framework: IProcessor/IStreamProcessor interfaces, AbstractProcessor<T> base with event pipeline, XmlProcessor (XmlReader), CsvProcessor + CsvParser (RFC 4180), HttpProcessor (download decorator), ZipProcessor (extraction decorator), decorator composition pattern

### Time
- **Birko.Time.Abstractions** - Clock abstraction (IDateTimeProvider, SystemDateTimeProvider, TestDateTimeProvider) with zero dependencies
- **Birko.Time** - Time zone conversion (ITimeZoneConverter), business calendar (IBusinessCalendar) with holidays and working hours (imports Birko.Time.Abstractions)
- **Birko.Time.Tests** - Unit tests for time utilities (providers, calendars, holidays, working hours, business calendar)

### Localization
- **Birko.Localization** - Translation framework with culture fallback chains, CLDR pluralization, JSON/RESX/InMemory providers
- **Birko.Localization.Data** - Database-backed translation provider (any Birko.Data store), namespace scoping, TTL cache
- **Birko.Data.Localization** - Entity-level localization: ILocalizable interface, EntityTranslationModel, store decorator wrappers (sync/async, singular/bulk)
- **Birko.Localization.Tests** - Unit tests for localization (providers, pluralizer, settings, formatters)
- **Birko.Localization.Data.Tests** - Unit tests for database translation provider (CRUD, namespaces, caching, filters)
- **Birko.Data.Localization.Tests** - Unit tests for entity localization (model, filter, sync/async wrapper tests)
- **Birko.Communication.Modbus.Tests** - Unit tests for Modbus communication (RTU/TCP framing, function codes, CRC, error handling)
- **Birko.Communication.OAuth.Tests** - Unit tests for OAuth2 client (settings, token, PKCE, flows, DelegatingHandler)

## Architecture

### Store Hierarchy
```
AbstractStore
    -> AbstractBulkStore (sync)
AbstractAsyncStore
    -> AbstractAsyncBulkStore (async)
```

### SQL Stores
```
DataBaseStore<DB,T>
    -> DataBaseBulkStore<DB,T> (sync)

AsyncDataBaseStore<DB,T>
    -> AsyncDataBaseBulkStore<DB,T> (async)
```

### Repository Hierarchy
```
AbstractRepository
    -> AbstractBulkRepository (sync)
AbstractAsyncRepository
    -> AbstractAsyncBulkRepository (async)
```

## Project Documentation

Each project has its own CLAUDE.md with specific details:

| Project | CLAUDE.md Location |
|---------|-------------------|
| Birko.Contracts | [../Birko.Contracts/CLAUDE.md](../Birko.Contracts/CLAUDE.md) |
| Birko.Data.Core | [../Birko.Data.Core/CLAUDE.md](../Birko.Data.Core/CLAUDE.md) |
| Birko.Configuration | [../Birko.Configuration/CLAUDE.md](../Birko.Configuration/CLAUDE.md) |
| Birko.Data.Stores | [../Birko.Data.Stores/CLAUDE.md](../Birko.Data.Stores/CLAUDE.md) |
| Birko.Data.Repositories | [../Birko.Data.Repositories/CLAUDE.md](../Birko.Data.Repositories/CLAUDE.md) |
| Birko.Data.Processors | [../Birko.Data.Processors/CLAUDE.md](../Birko.Data.Processors/CLAUDE.md) |
| Birko.Data.Processors.Tests | [../Birko.Data.Processors.Tests/CLAUDE.md](../Birko.Data.Processors.Tests/CLAUDE.md) |
| Birko.Data.SQL | [../Birko.Data.SQL/CLAUDE.md](../Birko.Data.SQL/CLAUDE.md) |
| Birko.Data.SQL.MSSql | [../Birko.Data.SQL.MSSql/CLAUDE.md](../Birko.Data.SQL.MSSql/CLAUDE.md) |
| Birko.Data.SQL.PostgreSQL | [../Birko.Data.SQL.PostgreSQL/CLAUDE.md](../Birko.Data.SQL.PostgreSQL/CLAUDE.md) |
| Birko.Data.SQL.MySQL | [../Birko.Data.SQL.MySQL/CLAUDE.md](../Birko.Data.SQL.MySQL/CLAUDE.md) |
| Birko.Data.SQL.SqLite | [../Birko.Data.SQL.SqLite/CLAUDE.md](../Birko.Data.SQL.SqLite/CLAUDE.md) |
| Birko.Data.SQL.View | [../Birko.Data.SQL.View/CLAUDE.md](../Birko.Data.SQL.View/CLAUDE.md) |
| Birko.Data.SQL.MSSql.View | [../Birko.Data.SQL.MSSql.View/CLAUDE.md](../Birko.Data.SQL.MSSql.View/CLAUDE.md) |
| Birko.Data.SQL.PostgreSQL.View | [../Birko.Data.SQL.PostgreSQL.View/CLAUDE.md](../Birko.Data.SQL.PostgreSQL.View/CLAUDE.md) |
| Birko.Data.SQL.MySQL.View | [../Birko.Data.SQL.MySQL.View/CLAUDE.md](../Birko.Data.SQL.MySQL.View/CLAUDE.md) |
| Birko.Data.SQL.SqLite.View | [../Birko.Data.SQL.SqLite.View/CLAUDE.md](../Birko.Data.SQL.SqLite.View/CLAUDE.md) |
| Birko.Data.SQL.ViewModel | [../Birko.Data.SQL.ViewModel/CLAUDE.md](../Birko.Data.SQL.ViewModel/CLAUDE.md) |
| Birko.Data.JSON | [../Birko.Data.JSON/CLAUDE.md](../Birko.Data.JSON/CLAUDE.md) |
| Birko.Data.ElasticSearch | [../Birko.Data.ElasticSearch/CLAUDE.md](../Birko.Data.ElasticSearch/CLAUDE.md) |
| Birko.Data.MongoDB | [../Birko.Data.MongoDB/CLAUDE.md](../Birko.Data.MongoDB/CLAUDE.md) |
| Birko.Data.RavenDB | [../Birko.Data.RavenDB/CLAUDE.md](../Birko.Data.RavenDB/CLAUDE.md) |
| Birko.Data.InfluxDB | [../Birko.Data.InfluxDB/CLAUDE.md](../Birko.Data.InfluxDB/CLAUDE.md) |
| Birko.Data.TimescaleDB | [../Birko.Data.TimescaleDB/CLAUDE.md](../Birko.Data.TimescaleDB/CLAUDE.md) |
| Birko.Data.ViewModel | [../Birko.Data.ViewModel/CLAUDE.md](../Birko.Data.ViewModel/CLAUDE.md) |
| Birko.Data.ElasticSearch.ViewModel | [../Birko.Data.ElasticSearch.ViewModel/CLAUDE.md](../Birko.Data.ElasticSearch.ViewModel/CLAUDE.md) |
| Birko.Data.InfluxDB.ViewModel | [../Birko.Data.InfluxDB.ViewModel/CLAUDE.md](../Birko.Data.InfluxDB.ViewModel/CLAUDE.md) |
| Birko.Data.JSON.ViewModel | [../Birko.Data.JSON.ViewModel/CLAUDE.md](../Birko.Data.JSON.ViewModel/CLAUDE.md) |
| Birko.Data.MongoDB.ViewModel | [../Birko.Data.MongoDB.ViewModel/CLAUDE.md](../Birko.Data.MongoDB.ViewModel/CLAUDE.md) |
| Birko.Data.RavenDB.ViewModel | [../Birko.Data.RavenDB.ViewModel/CLAUDE.md](../Birko.Data.RavenDB.ViewModel/CLAUDE.md) |
| Birko.Data.TimescaleDB.ViewModel | [../Birko.Data.TimescaleDB.ViewModel/CLAUDE.md](../Birko.Data.TimescaleDB.ViewModel/CLAUDE.md) |
| Birko.Data.Patterns | [../Birko.Data.Patterns/CLAUDE.md](../Birko.Data.Patterns/CLAUDE.md) |
| Birko.Data.Migrations | [../Birko.Data.Migrations/CLAUDE.md](../Birko.Data.Migrations/CLAUDE.md) |
| Birko.Data.Migrations.SQL | [../Birko.Data.Migrations.SQL/CLAUDE.md](../Birko.Data.Migrations.SQL/CLAUDE.md) |
| Birko.Data.Migrations.ElasticSearch | [../Birko.Data.Migrations.ElasticSearch/CLAUDE.md](../Birko.Data.Migrations.ElasticSearch/CLAUDE.md) |
| Birko.Data.Migrations.MongoDB | [../Birko.Data.Migrations.MongoDB/CLAUDE.md](../Birko.Data.Migrations.MongoDB/CLAUDE.md) |
| Birko.Data.Migrations.RavenDB | [../Birko.Data.Migrations.RavenDB/CLAUDE.md](../Birko.Data.Migrations.RavenDB/CLAUDE.md) |
| Birko.Data.Migrations.InfluxDB | [../Birko.Data.Migrations.InfluxDB/CLAUDE.md](../Birko.Data.Migrations.InfluxDB/CLAUDE.md) |
| Birko.Data.Migrations.TimescaleDB | [../Birko.Data.Migrations.TimescaleDB/CLAUDE.md](../Birko.Data.Migrations.TimescaleDB/CLAUDE.md) |
| Birko.Data.Sync | [../Birko.Data.Sync/CLAUDE.md](../Birko.Data.Sync/CLAUDE.md) |
| Birko.Data.Sync.Sql | [../Birko.Data.Sync.Sql/CLAUDE.md](../Birko.Data.Sync.Sql/CLAUDE.md) |
| Birko.Data.Sync.ElasticSearch | [../Birko.Data.Sync.ElasticSearch/CLAUDE.md](../Birko.Data.Sync.ElasticSearch/CLAUDE.md) |
| Birko.Data.Sync.MongoDb | [../Birko.Data.Sync.MongoDb/CLAUDE.md](../Birko.Data.Sync.MongoDb/CLAUDE.md) |
| Birko.Data.Sync.RavenDB | [../Birko.Data.Sync.RavenDB/CLAUDE.md](../Birko.Data.Sync.RavenDB/CLAUDE.md) |
| Birko.Data.Sync.Json | [../Birko.Data.Sync.Json/CLAUDE.md](../Birko.Data.Sync.Json/CLAUDE.md) |
| Birko.Data.Sync.Tenant | [../Birko.Data.Sync.Tenant/CLAUDE.md](../Birko.Data.Sync.Tenant/CLAUDE.md) |
| Birko.Data.Aggregates | [../Birko.Data.Aggregates/CLAUDE.md](../Birko.Data.Aggregates/CLAUDE.md) |
| Birko.Data.Aggregates.Tests | [../Birko.Data.Aggregates.Tests/CLAUDE.md](../Birko.Data.Aggregates.Tests/CLAUDE.md) |
| Birko.Data.Tenant | [../Birko.Data.Tenant/CLAUDE.md](../Birko.Data.Tenant/CLAUDE.md) |
| Birko.Data.EventSourcing | [../Birko.Data.EventSourcing/CLAUDE.md](../Birko.Data.EventSourcing/CLAUDE.md) |
| Birko.Structures | [../Birko.Structures/CLAUDE.md](../Birko.Structures/CLAUDE.md) |
| Birko.Helpers | [../Birko.Helpers/CLAUDE.md](../Birko.Helpers/CLAUDE.md) |
| Birko.Data.Repositories | [../Birko.Data.Repositories/CLAUDE.md](../Birko.Data.Repositories/CLAUDE.md) |
| Birko.Data.Tests | [../Birko.Data.Tests/CLAUDE.md](../Birko.Data.Tests/CLAUDE.md) |
| Birko.Data.SQL.Tests | [../Birko.Data.SQL.Tests/CLAUDE.md](../Birko.Data.SQL.Tests/CLAUDE.md) |
| Birko.Data.ElasticSearch.Tests | [../Birko.Data.ElasticSearch.Tests/CLAUDE.md](../Birko.Data.ElasticSearch.Tests/CLAUDE.md) |
| Birko.Helpers.Tests | [../Birko.Helpers.Tests/CLAUDE.md](../Birko.Helpers.Tests/CLAUDE.md) |
| Birko.Structures.Tests | [../Birko.Structures.Tests/CLAUDE.md](../Birko.Structures.Tests/CLAUDE.md) |
| Birko.BackgroundJobs.Tests | [../Birko.BackgroundJobs.Tests/CLAUDE.md](../Birko.BackgroundJobs.Tests/CLAUDE.md) |
| Birko.MessageQueue.Tests | [../Birko.MessageQueue.Tests/CLAUDE.md](../Birko.MessageQueue.Tests/CLAUDE.md) |
| Birko.MessageQueue.Redis.Tests | [../Birko.MessageQueue.Redis.Tests/CLAUDE.md](../Birko.MessageQueue.Redis.Tests/CLAUDE.md) |
| Birko.EventBus | [../Birko.EventBus/CLAUDE.md](../Birko.EventBus/CLAUDE.md) |
| Birko.EventBus.MessageQueue | [../Birko.EventBus.MessageQueue/CLAUDE.md](../Birko.EventBus.MessageQueue/CLAUDE.md) |
| Birko.EventBus.Outbox | [../Birko.EventBus.Outbox/CLAUDE.md](../Birko.EventBus.Outbox/CLAUDE.md) |
| Birko.EventBus.EventSourcing | [../Birko.EventBus.EventSourcing/CLAUDE.md](../Birko.EventBus.EventSourcing/CLAUDE.md) |
| Birko.EventBus.Tests | [../Birko.EventBus.Tests/CLAUDE.md](../Birko.EventBus.Tests/CLAUDE.md) |
| Birko.Health | [../Birko.Health/CLAUDE.md](../Birko.Health/CLAUDE.md) |
| Birko.Health.Data | [../Birko.Health.Data/CLAUDE.md](../Birko.Health.Data/CLAUDE.md) |
| Birko.Health.Redis | [../Birko.Health.Redis/CLAUDE.md](../Birko.Health.Redis/CLAUDE.md) |
| Birko.Health.Azure | [../Birko.Health.Azure/CLAUDE.md](../Birko.Health.Azure/CLAUDE.md) |
| Birko.Health.Tests | [../Birko.Health.Tests/CLAUDE.md](../Birko.Health.Tests/CLAUDE.md) |
| Birko.Health.Azure | [../Birko.Health.Azure/CLAUDE.md](../Birko.Health.Azure/CLAUDE.md) |
| Birko.Health.Azure.Tests | [../Birko.Health.Azure.Tests/CLAUDE.md](../Birko.Health.Azure.Tests/CLAUDE.md) |
| Birko.Storage | [../Birko.Storage/CLAUDE.md](../Birko.Storage/CLAUDE.md) |
| Birko.Storage.AzureBlob | [../Birko.Storage.AzureBlob/CLAUDE.md](../Birko.Storage.AzureBlob/CLAUDE.md) |
| Birko.Telemetry | [../Birko.Telemetry/CLAUDE.md](../Birko.Telemetry/CLAUDE.md) |
| Birko.Telemetry.OpenTelemetry | [../Birko.Telemetry.OpenTelemetry/CLAUDE.md](../Birko.Telemetry.OpenTelemetry/CLAUDE.md) |
| Birko.Telemetry.Tests | [../Birko.Telemetry.Tests/CLAUDE.md](../Birko.Telemetry.Tests/CLAUDE.md) |
| Birko.Telemetry.OpenTelemetry.Tests | [../Birko.Telemetry.OpenTelemetry.Tests/CLAUDE.md](../Birko.Telemetry.OpenTelemetry.Tests/CLAUDE.md) |
| Birko.Security.AspNetCore.Tests | [../Birko.Security.AspNetCore.Tests/CLAUDE.md](../Birko.Security.AspNetCore.Tests/CLAUDE.md) |
| Birko.Storage.Tests | [../Birko.Storage.Tests/CLAUDE.md](../Birko.Storage.Tests/CLAUDE.md) |
| Birko.Storage.AzureBlob.Tests | [../Birko.Storage.AzureBlob.Tests/CLAUDE.md](../Birko.Storage.AzureBlob.Tests/CLAUDE.md) |
| Birko.Messaging | [../Birko.Messaging/CLAUDE.md](../Birko.Messaging/CLAUDE.md) |
| Birko.Messaging.Razor | [../Birko.Messaging.Razor/CLAUDE.md](../Birko.Messaging.Razor/CLAUDE.md) |
| Birko.Messaging.Tests | [../Birko.Messaging.Tests/CLAUDE.md](../Birko.Messaging.Tests/CLAUDE.md) |
| Birko.Messaging.Razor.Tests | [../Birko.Messaging.Razor.Tests/CLAUDE.md](../Birko.Messaging.Razor.Tests/CLAUDE.md) |
| Birko.Communication | [../Birko.Communication/CLAUDE.md](../Birko.Communication/CLAUDE.md) |
| Birko.Communication.Network | [../Birko.Communication.Network/CLAUDE.md](../Birko.Communication.Network/CLAUDE.md) |
| Birko.Communication.Hardware | [../Birko.Communication.Hardware/CLAUDE.md](../Birko.Communication.Hardware/CLAUDE.md) |
| Birko.Communication.Bluetooth | [../Birko.Communication.Bluetooth/CLAUDE.md](../Birko.Communication.Bluetooth/CLAUDE.md) |
| Birko.Communication.WebSocket | [../Birko.Communication.WebSocket/CLAUDE.md](../Birko.Communication.WebSocket/CLAUDE.md) |
| Birko.Communication.REST | [../Birko.Communication.REST/CLAUDE.md](../Birko.Communication.REST/CLAUDE.md) |
| Birko.Communication.SOAP | [../Birko.Communication.SOAP/CLAUDE.md](../Birko.Communication.SOAP/CLAUDE.md) |
| Birko.Communication.SSE | [../Birko.Communication.SSE/CLAUDE.md](../Birko.Communication.SSE/CLAUDE.md) |
| Birko.Communication.Modbus | [../Birko.Communication.Modbus/CLAUDE.md](../Birko.Communication.Modbus/CLAUDE.md) |
| Birko.Communication.OAuth | [../Birko.Communication.OAuth/CLAUDE.md](../Birko.Communication.OAuth/CLAUDE.md) |
| Birko.Communication.Camera | [../Birko.Communication.Camera/CLAUDE.md](../Birko.Communication.Camera/CLAUDE.md) |
| Birko.Communication.IR | [../Birko.Communication.IR/CLAUDE.md](../Birko.Communication.IR/CLAUDE.md) |
| Birko.Communication.NFC | [../Birko.Communication.NFC/CLAUDE.md](../Birko.Communication.NFC/CLAUDE.md) |
| Birko.Models | [../Birko.Models/CLAUDE.md](../Birko.Models/CLAUDE.md) |
| Birko.Models.Product | [../Birko.Models.Product/CLAUDE.md](../Birko.Models.Product/CLAUDE.md) |
| Birko.Models.Category | [../Birko.Models.Category/CLAUDE.md](../Birko.Models.Category/CLAUDE.md) |
| Birko.Models.SEO | [../Birko.Models.SEO/CLAUDE.md](../Birko.Models.SEO/CLAUDE.md) |
| Birko.Models.Accounting | [../Birko.Models.Accounting/CLAUDE.md](../Birko.Models.Accounting/CLAUDE.md) |
| Birko.Models.Customers | [../Birko.Models.Customers/CLAUDE.md](../Birko.Models.Customers/CLAUDE.md) |
| Birko.Models.Users | [../Birko.Models.Users/CLAUDE.md](../Birko.Models.Users/CLAUDE.md) |
| Birko.Models.Warehouse | [../Birko.Models.Warehouse/CLAUDE.md](../Birko.Models.Warehouse/CLAUDE.md) |
| Birko.Validation | [../Birko.Validation/CLAUDE.md](../Birko.Validation/CLAUDE.md) |
| Birko.Rules | [../Birko.Rules/CLAUDE.md](../Birko.Rules/CLAUDE.md) |
| Birko.Rules.Tests | [../Birko.Rules.Tests/CLAUDE.md](../Birko.Rules.Tests/CLAUDE.md) |
| Birko.CQRS | [../Birko.CQRS/CLAUDE.md](../Birko.CQRS/CLAUDE.md) |
| Birko.CQRS.Tests | [../Birko.CQRS.Tests/CLAUDE.md](../Birko.CQRS.Tests/CLAUDE.md) |
| Birko.Workflow | [../Birko.Workflow/CLAUDE.md](../Birko.Workflow/CLAUDE.md) |
| Birko.Workflow.SQL | [../Birko.Workflow.SQL/CLAUDE.md](../Birko.Workflow.SQL/CLAUDE.md) |
| Birko.Workflow.ElasticSearch | [../Birko.Workflow.ElasticSearch/CLAUDE.md](../Birko.Workflow.ElasticSearch/CLAUDE.md) |
| Birko.Workflow.MongoDB | [../Birko.Workflow.MongoDB/CLAUDE.md](../Birko.Workflow.MongoDB/CLAUDE.md) |
| Birko.Workflow.RavenDB | [../Birko.Workflow.RavenDB/CLAUDE.md](../Birko.Workflow.RavenDB/CLAUDE.md) |
| Birko.Workflow.JSON | [../Birko.Workflow.JSON/CLAUDE.md](../Birko.Workflow.JSON/CLAUDE.md) |
| Birko.Workflow.Tests | [../Birko.Workflow.Tests/CLAUDE.md](../Birko.Workflow.Tests/CLAUDE.md) |
| Birko.BackgroundJobs | [../Birko.BackgroundJobs/CLAUDE.md](../Birko.BackgroundJobs/CLAUDE.md) |
| Birko.BackgroundJobs.SQL | [../Birko.BackgroundJobs.SQL/CLAUDE.md](../Birko.BackgroundJobs.SQL/CLAUDE.md) |
| Birko.BackgroundJobs.ElasticSearch | [../Birko.BackgroundJobs.ElasticSearch/CLAUDE.md](../Birko.BackgroundJobs.ElasticSearch/CLAUDE.md) |
| Birko.BackgroundJobs.MongoDB | [../Birko.BackgroundJobs.MongoDB/CLAUDE.md](../Birko.BackgroundJobs.MongoDB/CLAUDE.md) |
| Birko.BackgroundJobs.RavenDB | [../Birko.BackgroundJobs.RavenDB/CLAUDE.md](../Birko.BackgroundJobs.RavenDB/CLAUDE.md) |
| Birko.BackgroundJobs.JSON | [../Birko.BackgroundJobs.JSON/CLAUDE.md](../Birko.BackgroundJobs.JSON/CLAUDE.md) |
| Birko.BackgroundJobs.Redis | [../Birko.BackgroundJobs.Redis/CLAUDE.md](../Birko.BackgroundJobs.Redis/CLAUDE.md) |
| Birko.MessageQueue | [../Birko.MessageQueue/CLAUDE.md](../Birko.MessageQueue/CLAUDE.md) |
| Birko.MessageQueue.InMemory | [../Birko.MessageQueue.InMemory/CLAUDE.md](../Birko.MessageQueue.InMemory/CLAUDE.md) |
| Birko.MessageQueue.MQTT | [../Birko.MessageQueue.MQTT/CLAUDE.md](../Birko.MessageQueue.MQTT/CLAUDE.md) |
| Birko.MessageQueue.Redis | [../Birko.MessageQueue.Redis/CLAUDE.md](../Birko.MessageQueue.Redis/CLAUDE.md) |
| Birko.Caching | [../Birko.Caching/CLAUDE.md](../Birko.Caching/CLAUDE.md) |
| Birko.Caching.Redis | [../Birko.Caching.Redis/CLAUDE.md](../Birko.Caching.Redis/CLAUDE.md) |
| Birko.Caching.Hybrid | [../Birko.Caching.Hybrid/CLAUDE.md](../Birko.Caching.Hybrid/CLAUDE.md) |
| Birko.Caching.Hybrid.Tests | [../Birko.Caching.Hybrid.Tests/CLAUDE.md](../Birko.Caching.Hybrid.Tests/CLAUDE.md) |
| Birko.Redis | [../Birko.Redis/CLAUDE.md](../Birko.Redis/CLAUDE.md) |
| Birko.Security | [../Birko.Security/CLAUDE.md](../Birko.Security/CLAUDE.md) |
| Birko.Security.Jwt | [../Birko.Security.Jwt/CLAUDE.md](../Birko.Security.Jwt/CLAUDE.md) |
| Birko.Security.AspNetCore | [../Birko.Security.AspNetCore/CLAUDE.md](../Birko.Security.AspNetCore/CLAUDE.md) |
| Birko.Security.BCrypt | [../Birko.Security.BCrypt/CLAUDE.md](../Birko.Security.BCrypt/CLAUDE.md) |
| Birko.Security.BCrypt.Tests | [../Birko.Security.BCrypt.Tests/CLAUDE.md](../Birko.Security.BCrypt.Tests/CLAUDE.md) |
| Birko.Security.Vault | [../Birko.Security.Vault/CLAUDE.md](../Birko.Security.Vault/CLAUDE.md) |
| Birko.Security.Vault.Tests | [../Birko.Security.Vault.Tests/CLAUDE.md](../Birko.Security.Vault.Tests/CLAUDE.md) |
| Birko.Security.AzureKeyVault | [../Birko.Security.AzureKeyVault/CLAUDE.md](../Birko.Security.AzureKeyVault/CLAUDE.md) |
| Birko.Security.AzureKeyVault.Tests | [../Birko.Security.AzureKeyVault.Tests/CLAUDE.md](../Birko.Security.AzureKeyVault.Tests/CLAUDE.md) |
| Birko.Security.NFC | [../Birko.Security.NFC/CLAUDE.md](../Birko.Security.NFC/CLAUDE.md) |
| Birko.Serialization | [../Birko.Serialization/CLAUDE.md](../Birko.Serialization/CLAUDE.md) |
| Birko.Serialization.Newtonsoft | [../Birko.Serialization.Newtonsoft/CLAUDE.md](../Birko.Serialization.Newtonsoft/CLAUDE.md) |
| Birko.Serialization.MessagePack | [../Birko.Serialization.MessagePack/CLAUDE.md](../Birko.Serialization.MessagePack/CLAUDE.md) |
| Birko.Serialization.Protobuf | [../Birko.Serialization.Protobuf/CLAUDE.md](../Birko.Serialization.Protobuf/CLAUDE.md) |
| Birko.Serialization.Tests | [../Birko.Serialization.Tests/CLAUDE.md](../Birko.Serialization.Tests/CLAUDE.md) |
| Birko.Time.Abstractions | [../Birko.Time.Abstractions/CLAUDE.md](../Birko.Time.Abstractions/CLAUDE.md) |
| Birko.Time | [../Birko.Time/CLAUDE.md](../Birko.Time/CLAUDE.md) |
| Birko.Time.Tests | [../Birko.Time.Tests/CLAUDE.md](../Birko.Time.Tests/CLAUDE.md) |
| Birko.Localization | [../Birko.Localization/CLAUDE.md](../Birko.Localization/CLAUDE.md) |
| Birko.Localization.Data | [../Birko.Localization.Data/CLAUDE.md](../Birko.Localization.Data/CLAUDE.md) |
| Birko.Localization.Tests | [../Birko.Localization.Tests/CLAUDE.md](../Birko.Localization.Tests/CLAUDE.md) |
| Birko.Localization.Data.Tests | [../Birko.Localization.Data.Tests/CLAUDE.md](../Birko.Localization.Data.Tests/CLAUDE.md) |
| Birko.Data.Localization | [../Birko.Data.Localization/CLAUDE.md](../Birko.Data.Localization/CLAUDE.md) |
| Birko.Data.Localization.Tests | [../Birko.Data.Localization.Tests/CLAUDE.md](../Birko.Data.Localization.Tests/CLAUDE.md) |
| Birko.Communication.Modbus.Tests | [../Birko.Communication.Modbus.Tests/CLAUDE.md](../Birko.Communication.Modbus.Tests/CLAUDE.md) |
| Birko.Communication.OAuth.Tests | [../Birko.Communication.OAuth.Tests/CLAUDE.md](../Birko.Communication.OAuth.Tests/CLAUDE.md) |
| Birko.Communication.IR.Tests | [../Birko.Communication.IR.Tests/CLAUDE.md](../Birko.Communication.IR.Tests/CLAUDE.md) |
| Birko.Communication.NFC.Tests | [../Birko.Communication.NFC.Tests/CLAUDE.md](../Birko.Communication.NFC.Tests/CLAUDE.md) |
| Birko.Security.NFC.Tests | [../Birko.Security.NFC.Tests/CLAUDE.md](../Birko.Security.NFC.Tests/CLAUDE.md) |

## Key Patterns

### Settings Chain (Birko.Configuration)
```
ISettings (GetId)
    -> Settings (Location, Name)
        -> PasswordSettings (+Password)
            -> RemoteSettings (+UserName, +Port, +UseSecure)
```

### Dependency Flow
```
Birko.Contracts (zero deps: ILoadable, ICopyable, IDefault, ITimestamped, IGuidEntity, ILogEntity, RetryPolicy)
  -> Birko.Configuration (Settings hierarchy, namespace Birko.Configuration)
  -> Birko.Data.Core (AbstractModel, ViewModels, Filters, Exceptions)
       -> Birko.Data.Stores (store interfaces, imports Configuration)
            -> Birko.Data.Repositories

Birko.Time.Abstractions (zero deps: IDateTimeProvider, SystemDateTimeProvider, TestDateTimeProvider)
  -> Birko.Time (calendars, working hours, time zones)
```

### Reference Implementations
- **ElasticSearch** store - Good reference for async/bulk operations
- **JSON** store - Good reference for file-based storage

## Important Notes

### Recent Updates

#### New Model Projects (2026-03-06)
Extracted reusable models from FisData.Stock into framework:
- **Birko.Models.Accounting** - Currency, Tax, PriceGroup, MeasureUnit
- **Birko.Models.Customers** - Address, Customer, InvoiceAddress
- **Birko.Models.Users** - User, UserAgenda, Agenda (functional modules, separate from ITenant)
- **Birko.Models.Warehouse** - Item, ItemVariant, Repository, WareHouseDocument
- **Birko.Models** - Added AbstractPercentage, AbstractTree, ValueData base classes

#### Recent Fixes (2026-03-05)
- Replaced `NativeAsyncDataBaseStore` with `AsyncDataBaseStore` in async stores and repos
- Fixed `AbstractAsyncStore.CreateAsync` return type: `Task` -> `Task<Guid>`
- Changed `Connector` property from `private set` to `protected set` in DataBaseStore and AsyncDataBaseStore
- Added parameterless constructor to `DataBaseRepository`
- Fixed PostgreSQL/MySQL stores settings handling

### Conventions
- All stores implement: IStore, IAsyncStore, IBulkStore, IAsyncBulkStore
- All repositories implement: IRepository, IAsyncRepository, IBulkRepository, IAsyncBulkRepository
- Use protected setters for properties that derived classes need to modify
- RemoteSettings should be passed via base.SetSettings(), not constructed inline

### Code Style
- **Avoid unnecessary nesting:** Use early returns (guard clauses) instead of wrapping entire method bodies in if blocks. Prefer `if (x == null) return;` over `if (x != null) { ... entire body ... }`.
- **No nullable warnings:** All new code must compile without nullable reference type warnings (CS8600–CS8605, CS8618, CS8625, etc.). Use proper null checks, null-forgiving operator (`!`) only when provably safe, or `?` annotations as appropriate.

## Documentation

### Framework Documentation
- [TODO.md](./TODO.md) - Planned features and enhancement roadmap
- [docs/](docs/) folder for detailed documentation:
  - [Architecture](docs/architecture.md)
  - [Store Implementation Guide](docs/store-implementation.md)
  - [Repository Implementation Guide](docs/repository-implementation.md)
  - [Migration Guide](docs/migrations.md)
  - [Data Patterns](docs/patterns.md)
  - [Caching](docs/caching.md)
  - [Validation](docs/validation.md)
  - [Background Jobs](docs/background-jobs.md)
  - [Message Queue](docs/message-queue.md)
  - [Event Bus](docs/event-bus.md)
  - [Event Sourcing](docs/event-sourcing.md)
  - [Storage](docs/storage.md)
  - [Messaging](docs/messaging.md)
  - [Telemetry](docs/telemetry.md)
  - [Security](docs/security.md)
  - [Rules Engine](docs/rules.md)
  - [Workflow Engine](docs/workflow.md)
  - [CQRS](docs/cqrs.md)
  - [Health Checks](docs/health.md)
  - [Data Processors](docs/processors.md)
  - [Serialization](docs/serialization.md)
  - [Data Sync](docs/sync.md)
  - [Time](docs/time.md)
  - [Localization](docs/localization.md)
  - [Multi-Tenancy](docs/tenant.md)
  - [Communication](docs/communication.md)
  - [Dependency Tree](docs/dependencies.md)
  - [Consumer Projects](docs/consumers.md)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of any project, update its README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to a project, update its CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### New Project Checklist
When creating a new project in the framework, every project directory must contain:

1. **`License.md`** — MIT license (Copyright 2026 František Bereň). Copy from any existing project.
2. **`README.md`** — Project name, overview, features, test framework (if test project), running instructions, and License section.
3. **`CLAUDE.md`** — Overview, project location, components, dependencies, and maintenance instructions.
4. **`.gitignore`** — Standard Visual Studio .gitignore. Copy from any existing project.

**GUID requirements for `.shproj` and `.projitems` files:**
- `ProjectGuid` in `.shproj` and `SharedGUID` in `.projitems` must be valid GUIDs containing **only hex characters** (`0-9`, `a-f`).
- Format: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` (8-4-4-4-12 characters). Do NOT use human-readable names or non-hex letters (`g-z`) in GUIDs.
- Each project must have a unique GUID. Generate a proper random GUID (e.g., `b3a8c1d4-e5f6-4a7b-9c0d-1e2f3a4b5c6d`).

### Solution & Workspace Registration
When adding a new project to the framework, it must be registered in both:

1. **`Birko.Framework.slnx`** — Add a `<Project>` entry inside the appropriate `<Folder>` (or create a new folder). Shared projects use `.shproj`, test projects use `.csproj`. Paths are relative to the `.slnx` file (e.g., `../Birko.NewProject/Birko.NewProject.shproj`).

2. **`Birko.Framework.code-workspace`** — Add a folder entry in the `"folders"` array with a `"name"` using the `"Group / Birko.ProjectName"` convention and a `"path"` relative to the workspace file (e.g., `"../Birko.NewProject"`). Keep entries sorted alphabetically within their group.

Existing folder groups in both files:
- **BackgroundJobs/** — Birko.BackgroundJobs
- **Caching/** — Birko.Caching, Birko.Caching.Redis, Birko.Caching.Hybrid
- **Communication/** — Birko.Communication.*
- **Data/** — Birko.Contracts, Birko.Data.Core, Birko.Configuration, Birko.Data.Stores, Birko.Data.Repositories
- **Health/** — Birko.Health, Birko.Health.Data, Birko.Health.Redis, Birko.Health.Azure
- **Data.Migrations/** — Birko.Data.Migrations.*
- **Data.NoSQL/** — ElasticSearch, InfluxDB, JSON, MongoDB, RavenDB, TimescaleDB stores
- **Data.Patterns/** — Birko.Data.Patterns, EventSourcing, Tenant
- **Data.SQL/** — Birko.Data.SQL, MSSql, MySQL, PostgreSQL, SqLite, View
- **Data.Sync/** — Birko.Data.Sync.*
- **Data.ViewModels/** — Birko.Data.*.ViewModel
- **Helpers/** — Birko.Helpers, Birko.Structures
- **Models/** — Birko.Models.*
- **Redis/** — Birko.Redis
- **Security/** — Birko.Security, Birko.Security.Jwt, Birko.Security.AspNetCore, Birko.Security.BCrypt, Birko.Security.Vault, Birko.Security.AzureKeyVault, Birko.Security.NFC
- **Serialization/** — Birko.Serialization, Birko.Serialization.Newtonsoft, Birko.Serialization.MessagePack, Birko.Serialization.Protobuf
- **Storage/** — Birko.Storage, Birko.Storage.AzureBlob
- **Telemetry/** — Birko.Telemetry, Birko.Telemetry.OpenTelemetry
- **Tests/** — All *.Tests projects
- **CQRS/** — Birko.CQRS
- **Rules/** — Birko.Rules
- **Validation/** — Birko.Validation
- **Time/** — Birko.Time.Abstractions, Birko.Time
- **Workflow/** — Birko.Workflow, Birko.Workflow.SQL, Birko.Workflow.ElasticSearch, Birko.Workflow.MongoDB, Birko.Workflow.RavenDB, Birko.Workflow.JSON

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions

### Health Check Requirements
When creating a new project that connects to an external service (database, cache, cloud API, message broker, etc.), **automatically create a corresponding health check**:
- **Birko.Health.Data** — for database/data store providers (SQL, NoSQL, search, time-series)
- **Birko.Health.Redis** — for Redis-specific checks
- **Birko.Health.Azure** — for Azure cloud services (Blob Storage, Key Vault, Service Bus, etc.)
- **New Birko.Health.X project** — if the service doesn't fit existing health check projects (e.g., Birko.Health.Aws for AWS services)

Health check pattern:
1. Implement `IHealthCheck` with a lightweight connectivity probe (e.g., list with maxResults=1, ping, SELECT 1)
2. Dual constructors: `Func<T>` factory and singleton instance
3. Three-level status: Healthy (OK), Degraded (slow > threshold), Unhealthy (exception)
4. Include `latencyMs` in the result `Data` dictionary
5. Add unit tests for constructor validation, factory exception handling, and cancellation
6. Update `docs/health.md`, the health examples, and the Health tab in Program.cs
7. Register in solution (.slnx), workspace (.code-workspace), and framework .csproj
