# Birko Framework

A modular .NET framework providing data access, communication, and model infrastructure for enterprise applications. Built on .NET 10.0 with shared projects via .projitems.

## Features

- Multi-database support (SQL Server, PostgreSQL, MySQL, SQLite, MongoDB, RavenDB, Elasticsearch, InfluxDB, TimescaleDB, JSON)
- Sync and async store/repository abstractions with bulk operation support
- ViewModel layer with model-to-viewmodel mapping
- Database migrations framework
- Data synchronization across stores
- Multi-tenancy support
- Event sourcing pattern
- Communication layer (REST, SOAP, WebSocket, SSE, Bluetooth, Hardware, Network, Modbus, OAuth, Camera, IR, NFC)
- Domain model libraries (Product, Category, SEO, Accounting, Customers, Users, Warehouse)
- Fluent validation framework
- Caching with in-memory and Redis backends
- Security (password hashing with PBKDF2 and BCrypt, AES encryption, JWT tokens, RBAC, ASP.NET Core integration)
- Message queue abstractions (pub/sub, point-to-point, serialization, retry, dead letter)
- Event bus (in-process, distributed via MessageQueue, transactional outbox, event sourcing integration)
- Messaging (email via SMTP, SMS and push notification interfaces, string template engine)
- File/blob storage abstraction (local filesystem, cloud providers planned)
- Telemetry (store metrics via System.Diagnostics.Metrics, distributed tracing via ActivitySource, correlation ID middleware)
- OpenTelemetry integration (OTLP + Console exporters, auto-wires Birko meters/activity sources)
- Data-driven rules engine (composable rules, groups, contexts, SQL/Specification/Validation integration)
- Generic data processors (XML, CSV, HTTP, ZIP with decorator composition)
- Background job processing with pluggable persistent queues
- Data structures (trees, AVL trees, BST)
- Helper utilities and extensions (including RFC 4180 CSV parser)

## Project Structure

### Core

| Project | Description |
|---------|-------------|
| Birko.Framework | Main framework application |
| Birko.Data.Core | Models, ViewModels, Filters, Exceptions (foundation layer) |
| Birko.Data.Stores | Store interfaces/abstractions, Settings, OrderBy, StoreLocator |
| Birko.Data.Repositories | Repository interfaces/abstractions, RepositoryLocator, DI extensions |
| Birko.Models | Base entity and ViewModel classes |

### Data Layer

| Project | Description |
|---------|-------------|
| Birko.Data.SQL | SQL base classes (DataBaseStore, AsyncDataBaseStore, bulk variants) |
| Birko.Data.SQL.MSSql | SQL Server implementation |
| Birko.Data.SQL.PostgreSQL | PostgreSQL implementation |
| Birko.Data.SQL.MySQL | MySQL implementation |
| Birko.Data.SQL.SqLite | SQLite implementation |
| Birko.Data.JSON | JSON file-based storage |
| Birko.Data.ElasticSearch | Elasticsearch repository/store |
| Birko.Data.MongoDB | MongoDB repository/store |
| Birko.Data.RavenDB | RavenDB repository/store |
| Birko.Data.InfluxDB | InfluxDB time-series database |
| Birko.Data.TimescaleDB | TimescaleDB implementation |

### Data Features

| Project | Description |
|---------|-------------|
| Birko.Data.Patterns | Cross-cutting patterns (Unit of Work, Soft Delete, Audit, Paging) |
| Birko.Data.Migrations | Database migration framework (SQL, ES, MongoDB, RavenDB, InfluxDB, TimescaleDB) |
| Birko.Data.Sync | Data synchronization (SQL, ES, MongoDB, RavenDB, JSON, Tenant) |
| Birko.Data.Tenant | Multi-tenancy support |
| Birko.Data.EventSourcing | Event sourcing pattern |
| Birko.Data.SQL.View | SQL view generation |

### ViewModel Layer

| Project | Description |
|---------|-------------|
| Birko.Data.ViewModel | Base ViewModel repository abstractions |
| Birko.Data.SQL.ViewModel | SQL ViewModel repositories |
| Platform-specific ViewModel projects | ES, InfluxDB, JSON, MongoDB, RavenDB, TimescaleDB |

### Models

| Project | Description |
|---------|-------------|
| Birko.Models.Product | Product, variants, images, pricing |
| Birko.Models.Category | Categories with hierarchical tree support |
| Birko.Models.SEO | SEO metadata, URL aliases, sitemaps |
| Birko.Models.Accounting | Currency, Tax, PriceGroup, MeasureUnit |
| Birko.Models.Customers | Address, Customer, InvoiceAddress |
| Birko.Models.Users | User, UserLogin, UserProfile, Role, RolePermission, UserRole, Agenda |
| Birko.Models.Warehouse | Items, variants, repositories, documents |

### Communication

| Project | Description |
|---------|-------------|
| Birko.Communication | Base communication interfaces |
| Birko.Communication.REST | REST API client |
| Birko.Communication.SOAP | SOAP client |
| Birko.Communication.WebSocket | WebSocket implementation |
| Birko.Communication.SSE | Server-Sent Events |
| Birko.Communication.Network | Network communication |
| Birko.Communication.Hardware | Hardware communication |
| Birko.Communication.Bluetooth | Bluetooth communication |
| Birko.Communication.Modbus | Modbus RTU/TCP communication (serial/network, function codes 01-06/15-16) |
| Birko.Communication.OAuth | OAuth2 client (Client Credentials, Auth Code, PKCE, Device Code) |
| Birko.Communication.Camera | Camera frame capture (FFmpeg-based JPEG snapshots) |
| Birko.Communication.IR | Consumer IR (NEC, Samsung, RC5 protocols, pluggable transports) |
| Birko.Communication.NFC | NFC/RFID tag reading (ISO 14443A, NDEF, Serial/HTTP/HID transports) |

### Cross-Cutting

| Project | Description |
|---------|-------------|
| Birko.Validation | Fluent validation framework |
| Birko.Caching | In-memory caching with ICache interface |
| Birko.Caching.Redis | Redis-backed cache |
| Birko.Redis | Shared Redis infrastructure (RedisSettings, RedisConnectionManager) |
| Birko.Security | Password hashing, AES encryption, RBAC interfaces |
| Birko.Security.Jwt | JWT token provider |
| Birko.Security.AspNetCore | ASP.NET Core integration (JWT Bearer auth, ICurrentUser, tenant middleware, permission filters) |
| Birko.Security.BCrypt | BCrypt password hashing (pure C#, configurable work factor) |
| Birko.Security.Vault | HashiCorp Vault secret provider (KV v1/v2, HTTP API) |
| Birko.Security.AzureKeyVault | Azure Key Vault secret provider (OAuth2, REST API) |
| Birko.Security.NFC | NFC-based authentication (tag-to-user mapping, JWT integration) |
| Birko.BackgroundJobs | Background job processing framework |
| Birko.BackgroundJobs.SQL | SQL-based persistent job queue |
| Birko.BackgroundJobs.ElasticSearch | Elasticsearch-based persistent job queue |
| Birko.BackgroundJobs.MongoDB | MongoDB-based persistent job queue |
| Birko.BackgroundJobs.RavenDB | RavenDB-based persistent job queue |
| Birko.BackgroundJobs.JSON | JSON file-based job queue (dev/testing) |
| Birko.BackgroundJobs.Redis | Redis-based persistent job queue |
| Birko.MessageQueue | Core message queue interfaces (pub/sub, point-to-point) |
| Birko.MessageQueue.InMemory | In-memory channel-based queue (testing/development) |
| Birko.MessageQueue.MQTT | MQTT implementation via MQTTnet (IoT, sensors, telemetry) |
| Birko.EventBus | Core event bus (in-process, pipelines, deduplication, DI) |
| Birko.EventBus.MessageQueue | Distributed event bus over MessageQueue providers |
| Birko.EventBus.Outbox | Transactional outbox pattern (at-least-once delivery) |
| Birko.EventBus.EventSourcing | EventStore-to-EventBus bridge and replay |
| Birko.Messaging | Email, SMS, push notification interfaces and SMTP sender |
| Birko.Storage | File/blob storage abstraction (local filesystem) |
| Birko.Telemetry | Store instrumentation (metrics, tracing), correlation ID middleware |
| Birko.Telemetry.OpenTelemetry | OpenTelemetry SDK integration (OTLP, Console exporters) |
| Birko.Rules | Data-driven rule engine (rules, groups, contexts, evaluator) |
| Birko.Data.Processors | Generic stream processors (XML, CSV, HTTP, ZIP, decorator composition) |
| Birko.Structures | Tree data structures (AVL, BST) |
| Birko.Helpers | Utility and extension methods, CsvParser |

### Tests

| Project | Description |
|---------|-------------|
| Birko.Data.Tests | Core store abstraction tests |
| Birko.Data.SQL.Tests | SQL connector, strategy, and expression tests |
| Birko.Data.ElasticSearch.Tests | Elasticsearch expression tests |
| Birko.Helpers.Tests | Helper utility tests |
| Birko.Structures.Tests | Tree data structure tests |
| Birko.BackgroundJobs.Tests | Background job processing tests |
| Birko.MessageQueue.Tests | Message queue tests (core, InMemory, MQTT) |
| Birko.EventBus.Tests | Event bus tests (core, distributed, outbox, event sourcing) |
| Birko.Security.AspNetCore.Tests | ASP.NET Core security integration tests (JWT, permissions, tenants) |
| Birko.Storage.Tests | File storage tests (core types, LocalFileStorage, extensions) |
| Birko.Messaging.Tests | Messaging tests (core types, email, templates) |
| Birko.Telemetry.Tests | Telemetry tests (conventions, store wrappers, metrics, middleware) |
| Birko.Security.BCrypt.Tests | BCrypt password hashing tests |
| Birko.Security.Vault.Tests | Vault secret provider tests |
| Birko.Security.AzureKeyVault.Tests | Azure Key Vault secret provider tests |
| Birko.Rules.Tests | Rule engine tests (core types, contexts, evaluator) |
| Birko.Data.Processors.Tests | Data processor tests (CSV parser, XML/CSV/ZIP processors, HTTP transport) |
| Birko.Telemetry.OpenTelemetry.Tests | OpenTelemetry integration tests (options, DI, providers) |
| Birko.Communication.Modbus.Tests | Modbus communication tests (RTU/TCP framing, CRC, error handling) |
| Birko.Communication.OAuth.Tests | OAuth2 client tests (settings, token, PKCE, flows) |
| Birko.Communication.IR.Tests | IR communication tests (NEC/Samsung/RC5 encode/decode, IrTiming) |
| Birko.Communication.NFC.Tests | NFC communication tests (tag data, NDEF parsing, ISO 14443A, HID transport) |
| Birko.Security.NFC.Tests | NFC authentication tests (enroll/authenticate/revoke, normalization, store) |

## Architecture

### Store Hierarchy

```
AbstractStore -> AbstractBulkStore (sync)
AbstractAsyncStore -> AbstractAsyncBulkStore (async)
```

### SQL Store Hierarchy

```
DataBaseStore<DB,T> -> DataBaseBulkStore<DB,T> (sync)
AsyncDataBaseStore<DB,T> -> AsyncDataBaseBulkStore<DB,T> (async)
```

### Repository Hierarchy

```
AbstractRepository -> AbstractBulkRepository (sync)
AbstractAsyncRepository -> AbstractAsyncBulkRepository (async)
```

## Getting Started

```bash
# Clone and build
dotnet build Birko.Framework.slnx
```

## Running Tests

```bash
dotnet test
```

## Documentation

- [Architecture](docs/architecture.md)
- [Store Implementation Guide](docs/store-implementation.md)
- [Repository Implementation Guide](docs/repository-implementation.md)
- [Migration Guide](docs/migrations.md)
- [Data Patterns Guide](docs/patterns.md) (Unit of Work, Soft Delete, Audit, Paging, Specifications, Concurrency)
- [Validation Guide](docs/validation.md)
- [Caching Guide](docs/caching.md)
- [Security Guide](docs/security.md) (Password hashing, AES encryption, JWT, ASP.NET Core integration)
- [Background Jobs Guide](docs/background-jobs.md)
- [Message Queue Guide](docs/message-queue.md)
- [Event Bus Guide](docs/event-bus.md) (In-process, Distributed, Outbox, Event Sourcing integration)
- [Event Sourcing Guide](docs/event-sourcing.md)
- [Storage Guide](docs/storage.md) (Local filesystem, cloud providers)
- [Messaging Guide](docs/messaging.md) (Email, SMS, Push, Templates)
- [Data Synchronization Guide](docs/sync.md)
- [Multi-Tenancy Guide](docs/tenant.md)
- [Telemetry Guide](docs/telemetry.md) (Store metrics, distributed tracing, correlation ID)
- [Rules Engine Guide](docs/rules.md) (Data-driven rules, groups, contexts, SQL/Spec/Validation integration)
- [Data Processors Guide](docs/processors.md) (XML, CSV, HTTP, ZIP, decorator composition)
- [TODO / Roadmap](TODO.md)

## License

Part of the Birko Framework.
