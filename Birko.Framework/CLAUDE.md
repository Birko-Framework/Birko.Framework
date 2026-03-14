# Birko Framework

## Overview
Birko Framework is a modular .NET framework providing data access, communication, and model infrastructure for enterprise applications.

## Project Structure

### Core Projects
- **Birko.Framework** - Main framework application (.NET 10.0, shared projects via .projitems)
- **Birko.Data** - Core data interfaces and abstract classes
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
- **Birko.Data.Patterns** - Cross-cutting patterns (Unit of Work, Soft Delete, Audit, Paging)
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
- **Birko.Data.Tenant** - Multi-tenancy support
- **Birko.Data.EventSourcing** - Event sourcing pattern implementation
- **Birko.Structures** - Data structures (trees, etc.)
- **Birko.Data.SQL.View** - SQL view generation
- **Birko.Helpers** - Data helper utilities
- **Birko.Data.Repositories** - Shared repository project (reserved)

### Tests
- **Birko.Data.Tests** - Unit tests for core data stores (AbstractAsyncStore)
- **Birko.Data.SQL.Tests** - Unit tests for SQL connectors, strategies, and expression parsing
- **Birko.Data.ElasticSearch.Tests** - Unit tests for Elasticsearch expression parsing
- **Birko.Helpers.Tests** - Unit tests for helper utilities (EnumerableHelper)
- **Birko.Structures.Tests** - Unit tests for data structures (AVL tree, BST)
- **Birko.BackgroundJobs.Tests** - Unit tests for background job processing (queue, dispatcher, executor, processor, scheduler)
- **Birko.MessageQueue.Tests** - Unit tests for message queue (core, InMemory, MQTT topics, serialization)

### Communication Layer
- **Birko.Communication** - Base communication interfaces
- **Birko.Communication.Network** - Network communication
- **Birko.Communication.Hardware** - Hardware communication
- **Birko.Communication.Bluetooth** - Bluetooth communication
- **Birko.Communication.WebSocket** - WebSocket implementation
- **Birko.Communication.REST** - REST API client
- **Birko.Communication.SOAP** - SOAP client
- **Birko.Communication.SSE** - Server-Sent Events

### Models
- **Birko.Models** - Base models (AbstractPercentage, AbstractTree, ValueData, SourceValue)
- **Birko.Models.Product** - Product models
- **Birko.Models.Category** - Category models
- **Birko.Models.SEO** - SEO models
- **Birko.Models.Accounting** - Accounting models (Currency, Tax, PriceGroup, MeasureUnit)
- **Birko.Models.Customers** - Customer models (Address, Customer, InvoiceAddress)
- **Birko.Models.Users** - User and Agenda models
- **Birko.Models.Warehouse** - Warehouse/Inventory models (Item, ItemVariant, Repository, WareHouseDocument)

### Validation
- **Birko.Validation** - Fluent validation framework (IValidator<T>, AbstractValidator<T>, built-in rules, store wrappers)

### Caching
- **Birko.Caching** - Unified caching interface (ICache, MemoryCache, CacheSerializer)
- **Birko.Caching.Redis** - Redis backend (RedisCache)

### Redis
- **Birko.Redis** - Shared Redis infrastructure (RedisSettings extending RemoteSettings, RedisConnectionManager)

### Security
- **Birko.Security** - Password hashing (PBKDF2), AES-256-GCM encryption, token provider interfaces, static token auth (moved from Birko.Communication.Authentication), RBAC interfaces
- **Birko.Security.Jwt** - JWT implementation of ITokenProvider

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

### Planned Projects
- **Birko.Caching.Hybrid** - L1 memory + L2 distributed cache
- **Birko.Telemetry** - Logging, metrics, distributed tracing
- **Birko.Messaging** - Email, SMS, push notifications
- **Birko.MessageQueue** - Message queues (RabbitMQ, Kafka, Azure Service Bus, AWS SQS, Redis Streams)
- **Birko.EventBus** - Event bus abstraction (in-process, distributed via MessageQueue, EventSourcing integration)
- **Birko.Security.BCrypt** - BCrypt password hashing
- **Birko.Storage** - File and blob storage abstraction
- **Birko.Workflow** - State machine and business process automation
- **Birko.Time** - Time zone, business calendar, working hours
- **Birko.Health** - Health checks and diagnostics
- **Birko.Serialization** - JSON, XML, binary serialization abstraction
- **Birko.Localization** - Translations and culture support
- **Birko.CQRS** - Command Query Responsibility Segregation

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
| Birko.Data | [../Birko.Data/CLAUDE.md](../Birko.Data/CLAUDE.md) |
| Birko.Data.SQL | [../Birko.Data.SQL/CLAUDE.md](../Birko.Data.SQL/CLAUDE.md) |
| Birko.Data.SQL.MSSql | [../Birko.Data.SQL.MSSql/CLAUDE.md](../Birko.Data.SQL.MSSql/CLAUDE.md) |
| Birko.Data.SQL.PostgreSQL | [../Birko.Data.SQL.PostgreSQL/CLAUDE.md](../Birko.Data.SQL.PostgreSQL/CLAUDE.md) |
| Birko.Data.SQL.MySQL | [../Birko.Data.SQL.MySQL/CLAUDE.md](../Birko.Data.SQL.MySQL/CLAUDE.md) |
| Birko.Data.SQL.SqLite | [../Birko.Data.SQL.SqLite/CLAUDE.md](../Birko.Data.SQL.SqLite/CLAUDE.md) |
| Birko.Data.SQL.View | [../Birko.Data.SQL.View/CLAUDE.md](../Birko.Data.SQL.View/CLAUDE.md) |
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
| Birko.Communication | [../Birko.Communication/CLAUDE.md](../Birko.Communication/CLAUDE.md) |
| Birko.Communication.Network | [../Birko.Communication.Network/CLAUDE.md](../Birko.Communication.Network/CLAUDE.md) |
| Birko.Communication.Hardware | [../Birko.Communication.Hardware/CLAUDE.md](../Birko.Communication.Hardware/CLAUDE.md) |
| Birko.Communication.Bluetooth | [../Birko.Communication.Bluetooth/CLAUDE.md](../Birko.Communication.Bluetooth/CLAUDE.md) |
| Birko.Communication.WebSocket | [../Birko.Communication.WebSocket/CLAUDE.md](../Birko.Communication.WebSocket/CLAUDE.md) |
| Birko.Communication.REST | [../Birko.Communication.REST/CLAUDE.md](../Birko.Communication.REST/CLAUDE.md) |
| Birko.Communication.SOAP | [../Birko.Communication.SOAP/CLAUDE.md](../Birko.Communication.SOAP/CLAUDE.md) |
| Birko.Communication.SSE | [../Birko.Communication.SSE/CLAUDE.md](../Birko.Communication.SSE/CLAUDE.md) |
| Birko.Models | [../Birko.Models/CLAUDE.md](../Birko.Models/CLAUDE.md) |
| Birko.Models.Product | [../Birko.Models.Product/CLAUDE.md](../Birko.Models.Product/CLAUDE.md) |
| Birko.Models.Category | [../Birko.Models.Category/CLAUDE.md](../Birko.Models.Category/CLAUDE.md) |
| Birko.Models.SEO | [../Birko.Models.SEO/CLAUDE.md](../Birko.Models.SEO/CLAUDE.md) |
| Birko.Models.Accounting | [../Birko.Models.Accounting/CLAUDE.md](../Birko.Models.Accounting/CLAUDE.md) |
| Birko.Models.Customers | [../Birko.Models.Customers/CLAUDE.md](../Birko.Models.Customers/CLAUDE.md) |
| Birko.Models.Users | [../Birko.Models.Users/CLAUDE.md](../Birko.Models.Users/CLAUDE.md) |
| Birko.Models.Warehouse | [../Birko.Models.Warehouse/CLAUDE.md](../Birko.Models.Warehouse/CLAUDE.md) |
| Birko.Validation | [../Birko.Validation/CLAUDE.md](../Birko.Validation/CLAUDE.md) |
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
| Birko.Caching | [../Birko.Caching/CLAUDE.md](../Birko.Caching/CLAUDE.md) |
| Birko.Caching.Redis | [../Birko.Caching.Redis/CLAUDE.md](../Birko.Caching.Redis/CLAUDE.md) |
| Birko.Redis | [../Birko.Redis/CLAUDE.md](../Birko.Redis/CLAUDE.md) |
| Birko.Security | [../Birko.Security/CLAUDE.md](../Birko.Security/CLAUDE.md) |
| Birko.Security.Jwt | [../Birko.Security.Jwt/CLAUDE.md](../Birko.Security.Jwt/CLAUDE.md) |

## Key Patterns

### Settings Chain
```
Settings -> PasswordSettings -> RemoteSettings
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
- [docs/](docs/) folder for detailed documentation:
  - [Architecture](docs/architecture.md)
  - [Store Implementation Guide](docs/store-implementation.md)
  - [Repository Implementation Guide](docs/repository-implementation.md)
  - [Migration Guide](docs/migrations.md)

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
- **Caching/** — Birko.Caching, Birko.Caching.Redis
- **Communication/** — Birko.Communication.*
- **Data/** — Birko.Data (core)
- **Data.Migrations/** — Birko.Data.Migrations.*
- **Data.NoSQL/** — ElasticSearch, InfluxDB, JSON, MongoDB, RavenDB, TimescaleDB stores
- **Data.Patterns/** — Birko.Data.Patterns, EventSourcing, Tenant
- **Data.SQL/** — Birko.Data.SQL, MSSql, MySQL, PostgreSQL, SqLite, View
- **Data.Sync/** — Birko.Data.Sync.*
- **Data.ViewModels/** — Birko.Data.*.ViewModel
- **Helpers/** — Birko.Helpers, Birko.Structures
- **Models/** — Birko.Models.*
- **Redis/** — Birko.Redis
- **Security/** — Birko.Security, Birko.Security.Jwt
- **Tests/** — All *.Tests projects
- **Validation/** — Birko.Validation

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
