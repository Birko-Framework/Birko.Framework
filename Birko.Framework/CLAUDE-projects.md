# Birko Framework — Project Catalog

## Core Projects
- **Birko.Framework** - Main framework application (.NET 10.0, shared projects via .projitems)
- **Birko.Contracts** - Pure interfaces (ILoadable, ICopyable, IDefault, ITimestamped) with zero dependencies
- **Birko.Data.Core** - Models, ViewModels, Filters, Exceptions (foundation layer, imports Birko.Contracts)
- **Birko.Configuration** - Settings hierarchy (Settings, PasswordSettings, RemoteSettings) in namespace `Birko.Configuration`, imports Birko.Contracts
- **Birko.Data.Stores** - Store interfaces/abstractions, OrderBy, StoreLocator (imports Birko.Configuration transitively)
- **Birko.Data.Repositories** - Repository interfaces/abstractions, RepositoryLocator, DI extensions
- **Birko.Models** - Base models and extensions

## Data Layer
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

## ViewModel Layer
- **Birko.Data.ViewModel** - Base ViewModel repository abstractions
- **Birko.Data.SQL.ViewModel** - SQL ViewModel repositories
- **Birko.Data.ElasticSearch.ViewModel** / **InfluxDB.ViewModel** / **JSON.ViewModel** / **MongoDB.ViewModel** / **RavenDB.ViewModel** / **TimescaleDB.ViewModel**

## Data Features
- **Birko.Data.Patterns** - Cross-cutting patterns (Unit of Work, Soft Delete, Audit, Timestamp, Paging)
- **Birko.Data.Migrations** + **.SQL** / **.ElasticSearch** / **.MongoDB** / **.RavenDB** / **.InfluxDB** / **.TimescaleDB**
- **Birko.Data.Sync** + **.Sql** / **.ElasticSearch** / **.MongoDb** / **.RavenDB** / **.Json** / **.Tenant**
- **Birko.Data.Aggregates** - SQL-NoSQL aggregate mapper (flatten/expand for sync)
- **Birko.Data.Tenant** - Multi-tenancy support
- **Birko.Data.EventSourcing** - Event sourcing pattern
- **Birko.Data.SQL.View** + **.MSSql.View** / **.PostgreSQL.View** / **.MySQL.View** / **.SqLite.View** - SQL view DDL
- **Birko.Data.SQL.View.Migrations** - Integration between SQL View definitions and the Migration framework (ViewSqlGenerator, ViewMigrationExtensions)
- **Birko.Data.SQL.Caching** - Query caching decorator for SQL stores (CachedAsyncDataBaseBulkStore, SqlCacheKeyBuilder, SqlCacheOptions)
- **Birko.Data.Processors** - Stream processors (XML, CSV, HTTP, ZIP) with decorator composition
- **Birko.Structures** - Data structures (trees, etc.)
- **Birko.Random** - Pluggable RNG (SystemRandom, CryptoRandom, XorShift, MersenneTwister, SplitMix, TestRandom), distributions, sequences (GuidV4/V7, NanoId, Snowflake, tokens), noise (Perlin, Simplex)
- **Birko.Helpers** - Data helper utilities

## Communication
- **Birko.Communication** - Base interfaces
- **Birko.Communication.Network** / **.Hardware** / **.Bluetooth** / **.WebSocket** / **.REST** / **.SOAP** / **.SSE**
- **Birko.Communication.Modbus** - Modbus RTU/TCP (serial/network, function codes 01-06/15-16)
- **Birko.Communication.OAuth** - OAuth2 client (Client Credentials, Auth Code, PKCE, Device Code, Refresh Token)
- **Birko.Communication.Camera** - Camera frame capture (FFmpeg-based JPEG snapshots)
- **Birko.Communication.IR** - Consumer IR (38 kHz, NEC/Samsung/RC5, pluggable transports)
- **Birko.Communication.NFC** - NFC/RFID tag reading (ISO 14443A, NDEF, pluggable transports)

## Messaging
- **Birko.Messaging** - Core interfaces (IMessageSender, IEmailSender, ISmsSender, IPushSender), SMTP, string templates
- **Birko.Messaging.Razor** - Razor template engine (RazorLight-based, .cshtml templates)

## Models
- **Birko.Models.Contracts** - Domain contract interfaces (ICatalogItem, IPriceable, IVariantable, ICategorizeable, IBatchable, ILocatable, IHierarchical, IDocument, IContactable, IAddressable)
- **Birko.Models** - Base models (AbstractPercentage, AbstractTree, ValueData, SourceValue) + Value Objects (Money, MoneyWithTax, Percentage, PostalAddress, Quantity)
- **Birko.Models.Product** / **.Category** / **.SEO**
- **Birko.Models.Accounting** - Currency, Tax, PriceGroup, MeasureUnit
- **Birko.Models.Customers** - Address, Customer, InvoiceAddress
- **Birko.Models.Users** - User, UserLogin, UserProfile, RBAC (Role, RolePermission, UserRole), Agenda
- **Birko.Models.Inventory** - StockItem, StockItemVariant, StorageLocation, StockMovement, InventoryDocument (clean, no SQL attrs)
- **Birko.Models.Pricing** - Currency, Tax, PriceGroup, PriceList, PriceListEntry, Discount (clean, no SQL attrs)
- **Birko.Models.SQL** - Fluent SQL mapping framework (ModelMap, IModelMapping, ModelMapRegistry) — replaces attribute-based mapping

## Validation & Rules
- **Birko.Validation** - Fluent validation (IValidator<T>, AbstractValidator<T>, built-in rules, store wrappers)
- **Birko.Rules** - Data-driven rule engine (IRule, RuleGroup, RuleSet, RuleEvaluator)

## CQRS & Workflow
- **Birko.CQRS** - Command/Query (ICommand, IQuery, IRequestHandler, IPipelineBehavior, IMediator)
- **Birko.Workflow** - State machine engine (WorkflowBuilder, WorkflowEngine, guards, actions, Mermaid/DOT)
- **Birko.Workflow.SQL** / **.ElasticSearch** / **.MongoDB** / **.RavenDB** / **.JSON** - Persistence backends

## Serialization
- **Birko.Serialization** - Abstraction (ISerializer, SystemJsonSerializer, SystemXmlSerializer)
- **Birko.Serialization.Newtonsoft** / **.MessagePack** / **.Protobuf**

## Caching & Redis
- **Birko.Caching** - ICache, MemoryCache, CacheSerializer
- **Birko.Caching.Redis** - Redis backend
- **Birko.Caching.Hybrid** - L1 memory + L2 distributed two-tier cache
- **Birko.Redis** - Shared Redis infrastructure (RedisSettings, RedisConnectionManager)

## Security
- **Birko.Security** - PBKDF2 hashing, AES-256-GCM, token/secret provider interfaces, static token auth, RBAC interfaces
- **Birko.Security.BCrypt** - BCrypt hashing (pure C# Blowfish)
- **Birko.Security.Vault** - HashiCorp Vault (ISecretProvider, KV v1/v2)
- **Birko.Security.AzureKeyVault** - Azure Key Vault (ISecretProvider, OAuth2, REST API)
- **Birko.Security.Jwt** - JWT ITokenProvider
- **Birko.Security.AspNetCore** - ASP.NET Core integration (JWT Bearer, ICurrentUser, permissions, tenant middleware)
- **Birko.Security.NFC** - NFC-based authentication (tag-to-user mapping, enrollment, revocation)

## Background Jobs & Message Queue
- **Birko.BackgroundJobs** - Job interfaces, in-memory queue, processor, dispatcher, scheduler
- **Birko.BackgroundJobs.SQL** / **.ElasticSearch** / **.MongoDB** / **.RavenDB** / **.JSON** / **.Redis**
- **Birko.MessageQueue** - Core interfaces (IMessageQueue, IMessageProducer, IMessageConsumer), Pub/Sub, P2P
- **Birko.MessageQueue.InMemory** / **.MQTT** / **.Redis**

## Event Bus
- **Birko.EventBus** - Core (IEvent, IEventBus, IEventHandler), in-process bus, pipeline, deduplication
- **Birko.EventBus.MessageQueue** - Distributed event bus over Birko.MessageQueue
- **Birko.EventBus.Outbox** - Transactional outbox pattern
- **Birko.EventBus.EventSourcing** - EventStore-to-EventBus bridge

## Health
- **Birko.Health** - IHealthCheck, HealthCheckRunner, DiskSpace/Memory checks
- **Birko.Health.Data** - SQL, Elasticsearch, MongoDB, RavenDB, InfluxDB, Vault, MQTT, SMTP checks
- **Birko.Health.Redis** - Redis PING + latency
- **Birko.Health.Azure** - Blob Storage, Key Vault checks

## Storage & Telemetry
- **Birko.Storage** - IFileStorage, LocalFileStorage, presigned URLs
- **Birko.Storage.AzureBlob** - Azure Blob Storage (REST API, OAuth2, SAS)
- **Birko.Telemetry** - Store metrics, distributed tracing, correlation ID middleware
- **Birko.Telemetry.OpenTelemetry** - OTLP + Console exporters, ASP.NET Core instrumentation

## Time & Localization
- **Birko.Time.Abstractions** - IDateTimeProvider (zero deps)
- **Birko.Time** - Time zones, business calendar, holidays, working hours
- **Birko.Localization** - Translation framework, CLDR pluralization, JSON/RESX/InMemory providers
- **Birko.Localization.Data** - Database-backed translations, namespace scoping, TTL cache
- **Birko.Data.Localization** - Entity-level localization (ILocalizable, store decorator wrappers)

## Tests
All test projects use xUnit + FluentAssertions. Each `*.Tests` project has its own CLAUDE.md.
- Birko.Data.Tests, Birko.Data.SQL.Tests, Birko.Data.ElasticSearch.Tests
- Birko.Helpers.Tests, Birko.Structures.Tests
- Birko.BackgroundJobs.Tests, Birko.MessageQueue.Tests, Birko.MessageQueue.Redis.Tests
- Birko.EventBus.Tests, Birko.CQRS.Tests, Birko.Workflow.Tests
- Birko.Security.AspNetCore.Tests, Birko.Security.BCrypt.Tests, Birko.Security.Vault.Tests, Birko.Security.AzureKeyVault.Tests, Birko.Security.NFC.Tests
- Birko.Storage.Tests, Birko.Storage.AzureBlob.Tests
- Birko.Telemetry.Tests, Birko.Telemetry.OpenTelemetry.Tests
- Birko.Rules.Tests, Birko.Data.Processors.Tests, Birko.Data.Aggregates.Tests
- Birko.Health.Tests, Birko.Health.Azure.Tests
- Birko.Messaging.Tests, Birko.Messaging.Razor.Tests
- Birko.Serialization.Tests, Birko.Time.Tests, Birko.Caching.Hybrid.Tests
- Birko.Localization.Tests, Birko.Localization.Data.Tests, Birko.Data.Localization.Tests
- Birko.Communication.Modbus.Tests, Birko.Communication.OAuth.Tests, Birko.Communication.IR.Tests, Birko.Communication.NFC.Tests
- Birko.Data.MongoDB.Tests
- Birko.Random.Tests
- Birko.Data.RavenDB.Tests

## Per-Project CLAUDE.md
Each project has its own CLAUDE.md at `../Birko.{ProjectName}/CLAUDE.md` with specific details about components, dependencies, and conventions.
